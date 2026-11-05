using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using RevitHarness.Dialogs;

namespace RevitHarness.DialogWatcher
{
    public sealed class DialogWatcher : IDisposable
    {
        private static readonly string[] ErrorReporterProcesses = { "WerFault", "WerFaultSecure" };

        private readonly WatcherOptions options;
        private readonly DialogDecider decider;
        private readonly DialogLogger logger;
        private readonly PendingStore pending;
        private readonly UIA3Automation automation = new UIA3Automation();
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private readonly Dictionary<long, string> handled = new Dictionary<long, string>();
        private readonly Dictionary<long, int> unclosedPresses = new Dictionary<long, int>();
        private const int MaxPresses = 3;
        // Revit can take seconds to tear a dialog down while it loads add-ins. Returns as soon as the window is gone, so a wait this long costs nothing for a dialog that closes at once.
        private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(6);
        private readonly Dictionary<int, string> processNames = new Dictionary<int, string>();

        public int Processed { get; private set; }

        public DialogWatcher(WatcherOptions options)
        {
            this.options = options;
            decider = new DialogDecider(null, options.BaseDirectory);
            logger = new DialogLogger(options.BaseDirectory);
            pending = new PendingStore(options.BaseDirectory);
        }

        public void Run(CancellationToken cancel)
        {
            AutomationElement desktop = automation.GetDesktop();
            try
            {
                desktop.RegisterAutomationEvent(automation.EventLibrary.Window.WindowOpenedEvent,
                    TreeScope.Subtree, (e, id) => signal.Set());
            }
            catch (Exception exception) { Console.Error.WriteLine("WindowOpened subscription failed, polling only: " + exception.Message); }

            while (!cancel.IsCancellationRequested && !File.Exists(options.StopFilePath))
            {
                ScanOnce();
                signal.WaitOne(options.PollMilliseconds);
            }
        }

        public void ScanOnce()
        {
            var candidates = FindCandidates();
            if (candidates == null) return;
            PruneClosedWindows(candidates);
            foreach (var window in candidates)
            {
                try { Handle(window.Element, window.ProcessName, window.Pid); }
                catch (Exception exception) { Console.Error.WriteLine("handle failed: " + exception.Message); }
            }
            try { ProcessPending(candidates); }
            catch (Exception exception) { Console.Error.WriteLine("pending processing failed: " + exception.Message); }
        }

        // A window handle can be reused by a later dialog with the same text, and a process id by a later process.
        // Only handles and ids that are still present stay remembered.
        private void PruneClosedWindows(List<Candidate> candidates)
        {
            var hwnds = new HashSet<long>(candidates.Select(c => c.Hwnd));
            foreach (var gone in handled.Keys.Where(h => !hwnds.Contains(h)).ToList()) handled.Remove(gone);
            foreach (var gone in unclosedPresses.Keys.Where(h => !hwnds.Contains(h)).ToList()) unclosedPresses.Remove(gone);
        }

        private static readonly TimeSpan StaleGrace = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ApiEntryLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ApplyingLifetime = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(3);

        // Applies decisions requested through the CLI and marks pending dialogs whose window is gone as stale.
        private void ProcessPending(List<Candidate> candidates)
        {
            foreach (var entry in pending.List(true))
            {
                if (options.Pid != null && entry.Pid != options.Pid) continue;
                try { ProcessPendingEntry(entry, candidates); }
                catch (Exception exception) { Console.Error.WriteLine("pending " + entry.Id + " failed: " + exception.Message); }
            }
        }

        private void ProcessPendingEntry(PendingDialog entry, List<Candidate> candidates)
        {
            var window = entry.Hwnd == null ? null : candidates.FirstOrDefault(c =>
                c.Pid == entry.Pid && c.Hwnd == entry.Hwnd &&
                c.Title == entry.Title);
            if (entry.Status == PendingStatus.Applying)
            {
                if (DateTimeOffset.UtcNow - entry.UpdatedAt > ApplyingLifetime)
                    pending.MarkStale(entry.Id, "watcher stopped while applying the decision");
                return;
            }
            if (entry.Hwnd == null)
            {
                if (DateTimeOffset.UtcNow - entry.CreatedAt > ApiEntryLifetime)
                    pending.MarkStale(entry.Id, "api-sourced entry without a window expired");
                return;
            }
            if (window == null)
            {
                if (DateTimeOffset.UtcNow - entry.UpdatedAt > StaleGrace)
                    pending.MarkStale(entry.Id, "dialog closed before a decision was applied");
                return;
            }
            if (entry.Status == PendingStatus.DecisionRequested && entry.Decision != null) ApplyDecision(entry, window);
        }

        private void ApplyDecision(PendingDialog requested, Candidate window)
        {
            var entry = pending.ClaimDecision(requested.Id);
            if (entry == null) return;
            var button = entry.Decision!.Button;
            var shadow = new DialogDecision
            {
                Button = button,
                Record = new DialogLogRecord
                {
                    Source = DialogSource.Watcher, Dialog = entry.Dialog, Key = entry.Key, DecidedBy = "external",
                    PressedButton = button, LogId = entry.Id, Pid = entry.Pid
                }
            };
            if (options.DryRun) { Note(shadow, "dry-run", "would press '" + button.Name + "' for pending " + entry.Id); pending.Complete(entry.Id, false, "dry-run"); return; }
            var live = DialogCollector.Collect(window.Element, window.ProcessName);
            if (entry.Key != null && (!entry.Key.Equals(DialogKey.From(live)) || !DialogKey.SameBody(entry.Dialog, live)))
            { pending.Complete(entry.Id, false, "the dialog changed after it was reviewed; not pressed"); return; }
            var target = DialogCollector.FindButton(window.Element, button);
            if (target == null) { pending.Complete(entry.Id, false, "button not found in the live window"); return; }
            if (!target.Patterns.Invoke.IsSupported) { pending.Complete(entry.Id, false, "button does not support Invoke; not pressed"); return; }
            var error = InvokeGuard.Run(() => target.Patterns.Invoke.Pattern.Invoke(), InvokeTimeout);
            if (error != null) { shadow.Button = null; shadow.Record.PressedButton = null; }
            try { Observe(shadow, entry.Pid ?? 0, entry.Hwnd ?? 0, entry.Title); }
            catch (Exception exception) { Console.Error.WriteLine("outcome logging failed: " + exception.Message); }
            CompleteWithRetry(entry.Id, error == null, error);
        }

        // The button is already pressed (or not) by now, so the entry must not stay "applying" because the store was busy.
        private void CompleteWithRetry(string id, bool pressed, string? note)
        {
            for (var attempt = 1; ; attempt++)
            {
                try { pending.Complete(id, pressed, note); return; }
                catch (TimeoutException) when (attempt < 3) { }
            }
        }

        private sealed class Candidate
        {
            public AutomationElement Element = null!;
            public string ProcessName = string.Empty;
            public int Pid;
            public long Hwnd;
            public string Title = string.Empty;
        }

        private List<Candidate>? FindCandidates()
        {
            var result = new List<Candidate>();
            AutomationElement[] windows;
            try { windows = automation.GetDesktop().FindAllChildren(c => c.ByControlType(ControlType.Window)); }
            catch (Exception ex) { Console.Error.WriteLine("desktop enumeration failed: " + ex); return null; }
            try { return CollectCandidates(windows, result); }
            catch (Exception ex) { Console.Error.WriteLine("window enumeration failed: " + ex); return null; }
        }

        private List<Candidate> CollectCandidates(AutomationElement[] windows, List<Candidate> result)
        {
            var seenPids = new HashSet<int>();
            foreach (var w in windows)
            {
                int pid;
                try { pid = w.Properties.ProcessId.Value; } catch { continue; }
                seenPids.Add(pid);
                var name = ProcessName(pid);
                if (name == null) continue;
                var isReporter = options.Pid == null && ErrorReporterProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
                var isRevit = name.Equals("Revit", StringComparison.OrdinalIgnoreCase) && (options.Pid == null || options.Pid == pid);
                if (!isReporter && !isRevit && !options.ExtraPids.Contains(pid)) continue;
                if (isRevit && IsMainWindow(w, pid))
                {
                    foreach (var owned in OwnedWindows(w)) AddCandidate(result, owned, name, pid);
                    continue;
                }
                AddCandidate(result, w, name, pid);
            }
            foreach (var gone in processNames.Keys.Where(p => !seenPids.Contains(p)).ToList()) processNames.Remove(gone);
            return result;
        }

        // A window can vanish between enumeration and the property read; such a window is not a candidate.
        private static void AddCandidate(List<Candidate> result, AutomationElement element, string processName, int pid)
        {
            long hwnd;
            string title;
            try
            {
                hwnd = element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64();
                title = element.Name;
            }
            catch (COMException) { return; }
            result.Add(new Candidate { Element = element, ProcessName = processName, Pid = pid, Hwnd = hwnd, Title = title });
        }

        private static AutomationElement[] OwnedWindows(AutomationElement main) =>
            main.FindAllChildren(c => c.ByControlType(ControlType.Window));

        private static bool IsMainWindow(AutomationElement w, int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.MainWindowHandle != IntPtr.Zero && p.MainWindowHandle == w.Properties.NativeWindowHandle.ValueOrDefault;
            }
            catch { return false; }
        }

        private string? ProcessName(int pid)
        {
            if (processNames.TryGetValue(pid, out var cached)) return cached;
            try
            {
                using var p = Process.GetProcessById(pid);
                if (processNames.Count > 512) processNames.Clear();
                return processNames[pid] = p.ProcessName;
            }
            catch { return null; }
        }

        private string AllowlistStamp()
        {
            try { return File.GetLastWriteTimeUtc(DialogPaths.GetAllowlistPath(options.BaseDirectory)).Ticks.ToString(); }
            catch { return string.Empty; }
        }

        private void RegisterPending(DialogInfo info, string reason, string? screenshot, string? dump, long hwnd, int pid)
        {
            var open = PendingStore.From(info, DialogSource.Watcher, pid, hwnd, screenshot, dump, null, null, reason);
            pending.Register(open);
        }

        private void Handle(AutomationElement window, string processName, int pid)
        {
            var info = DialogCollector.Collect(window, processName);
            if (info.ButtonDetails.Count == 0) return;
            long hwnd = window.Properties.NativeWindowHandle.ValueOrDefault.ToInt64();
            var signature = info.Title + "\u0001" + info.Text + "\u0001" + string.Join("|", info.Buttons) + "\u0001" + AllowlistStamp() +
                "\u0001" + RuntimeIdOf(window);
            if (unclosedPresses.TryGetValue(hwnd, out var spent) && spent >= MaxPresses) return;
            if (handled.TryGetValue(hwnd, out var seen) && seen == signature) return;
            handled[hwnd] = signature;
            Processed++;

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff") + "_" + pid + "_" + hwnd.ToString("x");
            var shot = Screenshot.Capture(new IntPtr(hwnd), options.ScreenshotsDirectory, stamp);
            var known = DialogMatcher.FindDecisionRule(
                AllowlistLoader.Load(options.BaseDirectory).Rules, info, DialogSource.Watcher);
            var dump = known != null && !known.NoLearn ? null : UiaDump.Write(window, options.DumpsDirectory, stamp);

            var decision = decider.Decide(info, DialogSource.Watcher, shot, dump, hwnd, pid);
            // The decider failed before it could press or register anything: look at the dialog again on the next scan.
            if (!decision.ShouldPress && decision.PendingId == null) { handled.Remove(hwnd); return; }
            if (!decision.ShouldPress) return;
            if (decision.Button == null) { Skipped(decision, info, "result-code action cannot be pressed through UI Automation", shot, dump, hwnd, pid); return; }
            if (options.DryRun) { Note(decision, "dry-run", "would press '" + decision.Button.Name + "'"); return; }
            var target = DialogCollector.FindButton(window, decision.Button);
            if (target == null) { Skipped(decision, info, "button not found in the live window", shot, dump, hwnd, pid); return; }
            if (!target.Patterns.Invoke.IsSupported) { Skipped(decision, info, "button does not support Invoke; not pressed", shot, dump, hwnd, pid); return; }

            var error = InvokeGuard.Run(() => target.Patterns.Invoke.Pattern.Invoke(), InvokeTimeout);
            if (error != null) { Skipped(decision, info, error, shot, dump, hwnd, pid); return; }
            pending.SupersedeApiEntries(pid, "watcher " + decision.Record.LogId);
            // The window is gone, so its handle may be reused at once by the next dialog (Revit shows several in a row,
            // often with identical text). Forgetting it here keeps that successor from matching the old signature.
            if (Observe(decision, pid, hwnd, info.Title)) { unclosedPresses.Remove(hwnd); handled.Remove(hwnd); return; }
            // A click that lands while the dialog is still starting up is lost, so a reversible button is pressed again on the next scan.
            // An irreversible button is never pressed twice, and after MaxPresses the dialog is left to the caller as a pending entry.
            var presses = unclosedPresses.TryGetValue(hwnd, out var earlier) ? earlier + 1 : 1;
            if (presses < MaxPresses && Reversibility.IsReversible(decision.Button)) { unclosedPresses[hwnd] = presses; handled.Remove(hwnd); return; }
            unclosedPresses[hwnd] = MaxPresses;
            Skipped(decision, info, "'" + decision.Button.Name + "' was pressed and the dialog stayed open", shot, dump, hwnd, pid);
        }

        private void Skipped(DialogDecision decision, DialogInfo info, string why, string? shot, string? dump, long hwnd, int pid)
        {
            Note(decision, "skipped", why);
            try { RegisterPending(info, "rule " + decision.Record.MatchedRuleId + " matched but could not be applied: " + why, shot, dump, hwnd, pid); }
            catch (TimeoutException) { handled.Remove(hwnd); throw; }
        }

        private static string RuntimeIdOf(AutomationElement window)
        {
            try { return string.Join(".", window.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>()); }
            catch { return string.Empty; }
        }

        private bool Observe(DialogDecision decision, int pid, long hwnd, string? title)
        {
            bool closed = false;
            string? next = null;
            for (var waited = TimeSpan.Zero; waited < CloseWait && !closed; waited += TimeSpan.FromMilliseconds(150))
            {
                Thread.Sleep(150);
                closed = !IsWindowVisible(new IntPtr(hwnd));
            }
            foreach (var c in (FindCandidates() ?? new List<Candidate>()).Where(c => c.Pid == pid))
            {
                if (c.Hwnd != hwnd || (!closed && c.Title != title)) { next = c.Title; break; }
            }
            decider.LogOutcome(decision, closed, next);
            return closed;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        private void Note(DialogDecision decision, string kind, string note)
        {
            logger.Log(new DialogLogRecord
            {
                Source = DialogSource.Watcher,
                Dialog = decision.Record.Dialog,
                Key = decision.Record.Key,
                Kind = kind,
                Note = note + " (ref " + decision.Record.LogId + ")",
                PressedButton = decision.Button,
                Pid = decision.Record.Pid
            });
        }

        public void Dispose()
        {
            automation.Dispose();
            signal.Dispose();
        }
    }
}
