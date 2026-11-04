using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly Dictionary<int, string> processNames = new Dictionary<int, string>();

        public int Processed { get; private set; }

        public DialogWatcher(WatcherOptions options)
        {
            this.options = options;
            decider = new DialogDecider(null, options.LogDir, options.AllowlistPath);
            logger = new DialogLogger(options.LogDir);
            pending = new PendingStore(options.LogDir);
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
            foreach (var window in candidates)
            {
                try { Handle(window.Element, window.ProcessName, window.Pid); }
                catch (Exception exception) { Console.Error.WriteLine("handle failed: " + exception.Message); }
            }
            try { ProcessPending(candidates); }
            catch (Exception exception) { Console.Error.WriteLine("pending processing failed: " + exception.Message); }
        }

        private static readonly TimeSpan StaleGrace = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ApiEntryLifetime = TimeSpan.FromMinutes(5);

        // Applies decisions requested through the CLI and marks pending dialogs whose window is gone as stale.
        private void ProcessPending(List<Candidate> candidates)
        {
            foreach (var entry in pending.List(true))
            {
                try { ProcessPendingEntry(entry, candidates); }
                catch (Exception exception) { Console.Error.WriteLine("pending " + entry.Id + " failed: " + exception.Message); }
            }
        }

        private void ProcessPendingEntry(PendingDialog entry, List<Candidate> candidates)
        {
            var window = entry.Hwnd == null ? null : candidates.FirstOrDefault(c =>
                c.Pid == entry.Pid && c.Element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64() == entry.Hwnd &&
                c.Element.Name == entry.Title);
            if (entry.Hwnd == null)
            {
                if (DateTimeOffset.UtcNow - entry.CreatedAt > ApiEntryLifetime)
                    pending.MarkStale(entry.Id, "api-sourced entry without a window expired");
                return;
            }
            if (window == null)
            {
                if (entry.Status == PendingStatus.DecisionRequested || DateTimeOffset.UtcNow - entry.UpdatedAt > StaleGrace)
                    pending.MarkStale(entry.Id, "dialog closed before a decision was applied");
                return;
            }
            if (entry.Status == PendingStatus.DecisionRequested && entry.Decision != null) ApplyDecision(entry, window.Element);
        }

        private void ApplyDecision(PendingDialog entry, AutomationElement window)
        {
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
            var target = DialogCollector.FindButton(window, button);
            if (target == null) { pending.Complete(entry.Id, false, "button not found in the live window"); return; }
            if (!target.Patterns.Invoke.IsSupported) { pending.Complete(entry.Id, false, "button does not support Invoke; not pressed"); return; }
            var invoke = Task.Run(() => target.Patterns.Invoke.Pattern.Invoke());
            string? error = null;
            try { invoke.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException e) { error = e.InnerException?.Message ?? e.Message; }
            Observe(shadow, window, entry.Pid ?? 0, entry.Hwnd ?? 0, entry.Title);
            pending.Complete(entry.Id, error == null, error);
        }

        private sealed class Candidate
        {
            public AutomationElement Element = null!;
            public string ProcessName = string.Empty;
            public int Pid;
        }

        private List<Candidate> FindCandidates()
        {
            var result = new List<Candidate>();
            AutomationElement[] windows;
            try { windows = automation.GetDesktop().FindAllChildren(c => c.ByControlType(ControlType.Window)); }
            catch (Exception ex) { Console.Error.WriteLine("desktop enumeration failed: " + ex); return result; }
            foreach (var w in windows)
            {
                int pid;
                try { pid = w.Properties.ProcessId.Value; } catch { continue; }
                var name = ProcessName(pid);
                if (name == null) continue;
                var isReporter = ErrorReporterProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
                var isRevit = name.Equals("Revit", StringComparison.OrdinalIgnoreCase) && (options.Pid == null || options.Pid == pid);
                if (!isReporter && !isRevit && !options.ExtraPids.Contains(pid)) continue;
                if (isRevit && IsMainWindow(w, pid))
                {
                    foreach (var owned in OwnedWindows(w))
                        result.Add(new Candidate { Element = owned, ProcessName = name, Pid = pid });
                    continue;
                }
                result.Add(new Candidate { Element = w, ProcessName = name, Pid = pid });
            }
            return result;
        }

        private static AutomationElement[] OwnedWindows(AutomationElement main)
        {
            try { return main.FindAllChildren(c => c.ByControlType(ControlType.Window)); }
            catch { return Array.Empty<AutomationElement>(); }
        }

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
            try { return File.GetLastWriteTimeUtc(options.AllowlistPath ?? DialogPaths.GetAllowlistPath(options.LogDir)).Ticks.ToString(); }
            catch { return string.Empty; }
        }

        private void Handle(AutomationElement window, string processName, int pid)
        {
            var info = DialogCollector.Collect(window, processName);
            if (info.ButtonDetails.Count == 0) return;
            long hwnd = window.Properties.NativeWindowHandle.ValueOrDefault.ToInt64();
            var signature = info.Title + "\u0001" + info.Text + "\u0001" + string.Join("|", info.Buttons) + "\u0001" + AllowlistStamp();
            if (handled.TryGetValue(hwnd, out var seen) && seen == signature) return;
            handled[hwnd] = signature;
            Processed++;

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff") + "_" + pid + "_" + hwnd.ToString("x");
            var shot = Screenshot.Capture(new IntPtr(hwnd), options.ScreenshotsDirectory, stamp);
            var known = DialogMatcher.FindFirstMatch(
                AllowlistLoader.Load(options.LogDir, null, options.AllowlistPath).Rules, info, DialogSource.Watcher);
            var dump = known != null && !known.NoLearn ? null : UiaDump.Write(window, options.DumpsDirectory, stamp);

            var decision = decider.Decide(info, DialogSource.Watcher, shot, dump, hwnd, pid);
            if (!decision.ShouldPress) return;
            if (decision.Button == null) { Note(decision, "skipped", "result-code action cannot be pressed through UI Automation"); return; }
            if (options.DryRun) { Note(decision, "dry-run", "would press '" + decision.Button.Name + "'"); return; }
            var target = DialogCollector.FindButton(window, decision.Button);
            if (target == null) { Note(decision, "skipped", "button not found in the live window"); return; }
            if (!target.Patterns.Invoke.IsSupported) { Note(decision, "skipped", "button does not support Invoke; not pressed"); return; }

            var invoke = Task.Run(() => target.Patterns.Invoke.Pattern.Invoke());
            try { invoke.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException e) { Note(decision, "invoke-error", e.InnerException?.Message ?? e.Message); }
            pending.SupersedeApiEntries(pid, "watcher " + decision.Record.LogId);
            Observe(decision, window, pid, hwnd, info.Title);
        }

        private void Observe(DialogDecision decision, AutomationElement window, int pid, long hwnd, string? title)
        {
            bool closed = false;
            string? next = null;
            for (var i = 0; i < 10 && !closed; i++)
            {
                Thread.Sleep(150);
                closed = !IsAlive(window);
            }
            foreach (var c in FindCandidates().Where(c => c.Pid == pid))
            {
                var h = c.Element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64();
                var t = c.Element.Name;
                if (h != hwnd || (!closed && t != title)) { next = t; break; }
            }
            decider.LogOutcome(decision, closed, next);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        private static bool IsAlive(AutomationElement window)
        {
            try { return IsWindowVisible(window.Properties.NativeWindowHandle.Value); }
            catch { return false; }
        }

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
