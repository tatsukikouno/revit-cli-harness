using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RevitHarness.Dialogs
{
    public sealed class DecisionRequestResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public PendingDialog? Pending { get; set; }
    }

    // File-backed registry of unknown dialogs awaiting an external decision (dialogs\pending\<id>.json).
    // The harness never decides these itself: a caller requests a decision, the watcher presses the button and completes it.
    public sealed class PendingStore
    {
        private const int BodySummaryLength = 300;
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly string? baseDirectory;
        private readonly DialogLogger logger;

        public PendingStore(string? baseDirectory = null)
        {
            this.baseDirectory = baseDirectory;
            logger = new DialogLogger(baseDirectory);
        }

        public static string Summarize(string? text)
        {
            var flat = DialogKey.NormalizeText(text);
            return flat.Length <= BodySummaryLength ? flat : flat.Substring(0, BodySummaryLength) + "...";
        }

        public static PendingDialog From(DialogInfo dialog, DialogSource source, int? pid, long? hwnd, string? screenshotPath,
            string? dumpPath, string? logId, string? noLearnRuleId, string reason) => new PendingDialog
        {
            Source = source,
            Pid = pid,
            Hwnd = hwnd,
            Dialog = dialog,
            Key = DialogKey.From(dialog),
            Title = dialog.Title,
            AutomationId = dialog.AutomationId,
            Buttons = dialog.ButtonDetails.Count > 0
                ? dialog.ButtonDetails.ToList()
                : dialog.Buttons.Select(b => new DialogButton { Name = b }).ToList(),
            BodySummary = Summarize(dialog.Text),
            AddIn = AddInInfo.IsUnsignedAddInDialog(dialog) ? AddInInfo.Extract(dialog).ToList() : null,
            ScreenshotPath = screenshotPath,
            DumpPath = dumpPath,
            LogId = logId,
            NoLearnRuleId = noLearnRuleId,
            Reason = reason
        };

        public PendingDialog? Get(string id)
        {
            if (!IsSafeId(id)) return null;
            return Read(PathFor(id));
        }

        public IReadOnlyList<PendingDialog> List(bool openOnly)
        {
            var directory = DialogPaths.GetPendingDirectory(baseDirectory);
            if (!Directory.Exists(directory)) return Array.Empty<PendingDialog>();
            return Directory.GetFiles(directory, "*.json")
                .Select(Read).Where(p => p != null).Select(p => p!)
                .Where(p => !openOnly || p.IsOpen)
                .OrderBy(p => p.CreatedAt).ToList();
        }

        // The same dialog seen again (api hook and watcher, or a re-evaluation after an allowlist change) reuses the open entry.
        public PendingDialog Register(PendingDialog candidate)
        {
            return Locked(() =>
            {
                var open = List(true);
                var fingerprint = candidate.Key?.Fingerprint();
                var existing = open.FirstOrDefault(p => p.Source == candidate.Source && p.Pid == candidate.Pid &&
                    (candidate.Hwnd == null || p.Hwnd == null || p.Hwnd == candidate.Hwnd) &&
                    p.Key?.Fingerprint() == fingerprint);
                if (existing != null)
                {
                    existing.Hwnd = candidate.Hwnd ?? existing.Hwnd;
                    existing.ScreenshotPath = candidate.ScreenshotPath ?? existing.ScreenshotPath;
                    existing.DumpPath = candidate.DumpPath ?? existing.DumpPath;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    Write(existing);
                    return existing;
                }
                if (candidate.Source == DialogSource.Watcher) SupersedeApiEntries(open, candidate.Pid, candidate.Id);
                Write(candidate);
                return candidate;
            });
        }

        // The watcher answered a dialog itself, so the api hook's entry for the same process describes a dialog that is gone.
        public void SupersedeApiEntries(int? pid, string by) => Locked<object?>(() =>
        {
            SupersedeApiEntries(List(true), pid, by);
            return null;
        });

        private void SupersedeApiEntries(IEnumerable<PendingDialog> open, int? pid, string by)
        {
            foreach (var api in open.Where(p => p.Source == DialogSource.Api && p.Pid == pid))
                SetStale(api, "superseded by " + by);
        }

        // Validates a caller's decision and queues it for the watcher. The button must exist in the dialog.
        public DecisionRequestResult RequestDecision(string id, string? buttonName, string? buttonAutomationId, string? by,
            bool saveRule)
        {
            if (string.IsNullOrWhiteSpace(by)) return Fail("--by (decider name or model) is required");
            if (string.IsNullOrWhiteSpace(buttonName) && string.IsNullOrWhiteSpace(buttonAutomationId))
                return Fail("a button name or automationId is required");
            try { return RequestDecisionLocked(id, buttonName, buttonAutomationId, by!, saveRule); }
            catch (TimeoutException e) { return Fail(e.Message); }
        }

        private DecisionRequestResult RequestDecisionLocked(string id, string? buttonName, string? buttonAutomationId, string by, bool saveRule)
        {
            return Locked(() =>
            {
                var pending = Get(id);
                if (pending == null) return Fail("pending dialog not found: " + id);
                if (pending.Status == PendingStatus.Stale) return Fail("pending dialog is stale (" + pending.StaleReason + ")", pending);
                if (pending.Status != PendingStatus.Pending) return Fail("pending dialog is already " + pending.Status, pending);
                if (pending.Hwnd == null)
                    return Fail("no window handle recorded; decide the watcher-sourced pending entry instead", pending);
                var button = ResolveButton(pending, buttonName, buttonAutomationId);
                if (button == null)
                    return Fail("button not in dialog: " + (buttonName ?? buttonAutomationId) + ". Available: " +
                        string.Join(", ", pending.Buttons.Select(b => b.Name)), pending);
                pending.Status = PendingStatus.DecisionRequested;
                pending.Decision = new PendingDecisionRequest { Button = button, By = by.Trim(), SaveRule = saveRule };
                pending.UpdatedAt = DateTimeOffset.UtcNow;
                Write(pending);
                return new DecisionRequestResult { Ok = true, Pending = pending };
            });
        }

        // Waits for the watcher to apply a requested decision. When it does not pick the request up in time the request is
        // withdrawn and null is returned. A request the watcher already claimed cannot be withdrawn: the wait continues
        // for the apply grace period and returns the entry (possibly still applying) so the caller never reports a press
        // that happened as a timeout.
        public PendingDialog? WaitForResolution(string id, TimeSpan timeout, TimeSpan? pollInterval = null, TimeSpan? applyGrace = null)
        {
            var poll = pollInterval ?? TimeSpan.FromMilliseconds(200);
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var current = Get(id);
                // A null read is a file being swapped by a writer: poll again.
                if (current != null && current.Status != PendingStatus.DecisionRequested && current.Status != PendingStatus.Applying) return current;
                if (DateTime.UtcNow >= deadline)
                {
                    if (TryWithdrawRequest(id)) return null;
                    break;
                }
                System.Threading.Thread.Sleep(poll);
            }
            var graceEnd = DateTime.UtcNow + (applyGrace ?? ApplyGrace);
            while (DateTime.UtcNow < graceEnd)
            {
                var current = Get(id);
                if (current != null && current.Status == PendingStatus.DecisionRequested)
                {
                    if (TryWithdrawRequest(id)) return null;
                }
                else if (current != null && current.Status != PendingStatus.Applying) return current;
                System.Threading.Thread.Sleep(poll);
            }
            return Get(id);
        }

        private static readonly TimeSpan ApplyGrace = TimeSpan.FromSeconds(15);

        // True only when no request is outstanding afterwards. False when the watcher claimed or finished it, or the store was busy.
        public bool TryWithdrawRequest(string id)
        {
            try { return WithdrawLocked(id); }
            catch (TimeoutException) { return false; }
        }

        private bool WithdrawLocked(string id) => Locked(() =>
        {
            var pending = Get(id);
            if (pending == null || pending.Status == PendingStatus.Pending) return true;
            if (pending.Status != PendingStatus.DecisionRequested) return false;
            pending.Status = PendingStatus.Pending;
            pending.Decision = null;
            pending.UpdatedAt = DateTimeOffset.UtcNow;
            Write(pending);
            return true;
        });

        // Watcher side: takes the requested decision under the lock before pressing. Returns null when the request was
        // withdrawn or replaced, in which case nothing may be pressed.
        public PendingDialog? ClaimDecision(string id) => Locked(() =>
        {
            var pending = Get(id);
            if (pending == null || pending.Status != PendingStatus.DecisionRequested || pending.Decision == null) return null;
            pending.Status = PendingStatus.Applying;
            pending.UpdatedAt = DateTimeOffset.UtcNow;
            Write(pending);
            return pending;
        });

        public void MarkStale(string id, string reason) => Locked<object?>(() =>
        {
            var pending = Get(id);
            if (pending != null && pending.IsOpen) SetStale(pending, reason);
            return null;
        });


        // Watcher side: finishes a claimed decision. The request cannot change while it is claimed, so the logged button is the pressed one.
        // The requested button was pressed (or failed). Saves the rule when allowed and logs DecidedBy=external.
        public PendingDialog? Complete(string id, bool pressed, string? note = null)
        {
            return Locked(() =>
            {
                var pending = Get(id);
                if (pending == null || pending.Status != PendingStatus.Applying || pending.Decision == null) return pending;
                var request = pending.Decision;
                var resolution = new PendingResolution { By = request.By, PressedButton = pressed ? request.Button : null, Note = note };
                if (pressed && request.SaveRule) SaveRule(pending, request, resolution);
                pending.Resolution = resolution;
                pending.Status = pressed ? PendingStatus.Resolved : PendingStatus.Failed;
                pending.UpdatedAt = DateTimeOffset.UtcNow;
                Write(pending);
                logger.Log(new DialogLogRecord
                {
                    Source = pending.Source,
                    Dialog = pending.Dialog,
                    Key = pending.Key,
                    Pid = pending.Pid,
                    Kind = "decision",
                    DecidedBy = "external",
                    DeciderName = request.By,
                    PendingId = pending.Id,
                    MatchedRuleId = "unknown",
                    ActionTaken = pressed ? "button" : "none",
                    PressedButton = resolution.PressedButton,
                    LearnedRuleId = resolution.RuleId,
                    ScreenshotPath = pending.ScreenshotPath,
                    DumpPath = pending.DumpPath,
                    Note = resolution.RuleNotSavedReason ?? note
                });
                return pending;
            });
        }

        private void SaveRule(PendingDialog pending, PendingDecisionRequest request, PendingResolution resolution)
        {
            var button = request.Button;
            if (AddInInfo.IsUnsignedAddInDialog(pending.Dialog))
            { resolution.RuleNotSavedReason = "security dialog: add-in choices are never learned, write a rule with an addIn condition by hand"; return; }
            var noLearn = pending.NoLearnRuleId ?? DialogMatcher.FindFirstMatch(
                AllowlistLoader.Load(baseDirectory).Rules.Where(r => r.NoLearn), pending.Dialog, DialogSource.Watcher)?.Id;
            if (noLearn != null)
            { resolution.RuleNotSavedReason = "noLearn rule " + noLearn + " matches this dialog"; return; }
            if (!Reversibility.IsReversible(button))
            { resolution.RuleNotSavedReason = "button is not known to be reversible; write an allowlist rule by hand"; return; }
            var key = pending.Key ?? DialogKey.From(pending.Dialog);
            var rule = new AllowRule
            {
                Id = "learned-" + key.Fingerprint().Substring(0, 16),
                Target = DialogTarget.Watcher,
                Key = key,
                Action = new DialogAction { ButtonName = button.Name, ButtonAutomationId = button.AutomationId },
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "external:" + request.By,
                SourceLogRef = "pending:" + pending.Id,
                Note = "saved from pending dialog decision"
            };
            if (AllowlistStore.TryAddRule(rule, baseDirectory)) resolution.RuleId = rule.Id;
            else resolution.RuleNotSavedReason = "rule not added (already exists or allowlist unwritable)";
        }

        public static DialogButton? ResolveButton(PendingDialog pending, string? name, string? automationId)
        {
            var hasName = !string.IsNullOrWhiteSpace(name);
            var hasId = !string.IsNullOrWhiteSpace(automationId);
            if (!hasName && !hasId) return null;
            return pending.Buttons.FirstOrDefault(b =>
                (!hasName || string.Equals(b.Name, name, StringComparison.Ordinal)) &&
                (!hasId || string.Equals(b.AutomationId, automationId, StringComparison.Ordinal)));
        }

        private static DecisionRequestResult Fail(string error, PendingDialog? pending = null) =>
            new DecisionRequestResult { Ok = false, Error = error, Pending = pending };

        private void SetStale(PendingDialog pending, string reason)
        {
            pending.Status = PendingStatus.Stale;
            pending.StaleReason = reason;
            pending.Decision = null;
            pending.UpdatedAt = DateTimeOffset.UtcNow;
            Write(pending);
        }

        private string PathFor(string id) => Path.Combine(DialogPaths.GetPendingDirectory(baseDirectory), id + ".json");

        private static bool IsSafeId(string id) => !string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c == '-');

        private static PendingDialog? Read(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<PendingDialog>(stream);
            }
            catch { return null; }
        }

        private void Write(PendingDialog pending)
        {
            var path = PathFor(pending.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(pending, WriteOptions), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private T Locked<T>(Func<T> action)
        {
            var directory = DialogPaths.GetPendingDirectory(baseDirectory);
            Directory.CreateDirectory(directory);
            using var gate = CrossProcessLock.Acquire(Path.Combine(directory, ".lock"), TimeSpan.FromSeconds(10));
            if (!gate.Held) throw new TimeoutException("pending store busy (lock not acquired within 10 s)");
            return action();
        }
    }
}
