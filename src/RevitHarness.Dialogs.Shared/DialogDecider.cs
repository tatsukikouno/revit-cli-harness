using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RevitHarness.Dialogs
{
    public sealed class DecisionContext
    {
        public int Pid { get; set; } = Process.GetCurrentProcess().Id;
        public string? RevitVersion { get; set; }
        public string? OpenDocument { get; set; }
        public string? TaskId { get; set; } = Environment.GetEnvironmentVariable("REVIT_HARNESS_TASK_ID");
        public string? HarnessCommandId { get; set; } = Environment.GetEnvironmentVariable("REVIT_HARNESS_COMMAND_ID");
    }

    public sealed class DialogDecision
    {
        public bool ShouldPress => Button != null || !string.IsNullOrEmpty(ResultCode);
        public DialogButton? Button { get; set; }
        public string? ResultCode { get; set; }
        public DialogLogRecord Record { get; set; } = new DialogLogRecord();
        public string? PendingId { get; set; }
    }

    // The single entry point for the api and watcher paths: dialog in, action plus log out.
    // Only an allowlist rule presses a button. Anything else is registered as a pending dialog for an external decision.
    public sealed class DialogDecider
    {
        private readonly string? baseDirectory;
        private readonly string? allowlistPath;
        private readonly DialogLogger logger;
        private readonly DecisionContext context;
        private readonly PendingStore pendingStore;

        public DialogDecider(DecisionContext? context = null, string? baseDirectory = null, string? allowlistPath = null)
        {
            this.allowlistPath = allowlistPath;
            this.context = context ?? new DecisionContext();
            this.baseDirectory = baseDirectory;
            logger = new DialogLogger(baseDirectory);
            pendingStore = new PendingStore(baseDirectory);
        }

        public DialogDecision Decide(DialogInfo dialog, DialogSource source, string? screenshotPath = null,
            string? dumpPath = null, long? hwnd = null, int? dialogPid = null)
        {
            var key = DialogKey.From(dialog);
            var record = NewRecord(source, dialog, key);
            record.ScreenshotPath = screenshotPath;
            record.DumpPath = dumpPath;
            var decision = new DialogDecision { Record = record };
            try
            {
                var rules = AllowlistLoader.Load(baseDirectory, null, allowlistPath).Rules;
                var rule = DialogMatcher.FindFirstMatch(rules, dialog, source);
                record.MatchedRuleId = rule?.Id ?? "unknown";
                if (rule != null && !rule.NoLearn)
                {
                    record.DecidedBy = "rule";
                    Press(decision, ResolveButton(dialog, rule.Action.ButtonName, rule.Action.ButtonAutomationId),
                        rule.Action.ResultCode);
                    var hasCode = !string.IsNullOrEmpty(rule.Action.ResultCode);
                    record.ActionTaken = decision.Button != null ? "button" : hasCode ? "resultCode:" + rule.Action.ResultCode : "none";
                    if (!decision.ShouldPress) record.Note = "rule action not applicable to this dialog";
                    return Finish(decision);
                }

                var reason = rule != null ? "matched noLearn rule " + rule.Id : "no matching rule";
                if (AddInInfo.IsUnsignedAddInDialog(dialog))
                    reason += "; unsigned add-in not on the known list: " + string.Join(", ", AddInInfo.Extract(dialog));
                var pending = pendingStore.Register(PendingStore.From(dialog, source, dialogPid ?? context.Pid, hwnd, screenshotPath,
                    dumpPath, record.LogId, rule?.Id, reason));
                record.DecidedBy = "pending";
                record.ActionTaken = "none";
                record.PendingId = pending.Id;
                record.Note = reason;
                decision.PendingId = pending.Id;
                return Finish(decision);
            }
            catch (Exception exception)
            {
                record.ActionTaken = "none";
                record.Note = "decider error: " + exception.Message;
                decision.Button = null;
                decision.ResultCode = null;
                return Finish(decision);
            }
        }

        public void LogOutcome(DialogDecision decision, bool closed, string? nextDialogTitle = null)
        {
            var d = decision.Record;
            var outcome = NewRecord(d.Source, d.Dialog, d.Key ?? DialogKey.From(d.Dialog));
            outcome.Kind = "outcome";
            outcome.MatchedRuleId = d.MatchedRuleId;
            outcome.DecidedBy = d.DecidedBy;
            outcome.PressedButton = d.PressedButton;
            outcome.Note = d.LogId;
            outcome.Outcome = new DialogOutcome { Closed = closed, NextDialogTitle = nextDialogTitle };
            logger.Log(outcome);
        }

        private DialogLogRecord NewRecord(DialogSource source, DialogInfo dialog, DialogKey key) => new DialogLogRecord
        {
            Source = source,
            Dialog = dialog,
            Key = key,
            Pid = context.Pid,
            RevitVersion = context.RevitVersion,
            OpenDocument = context.OpenDocument,
            TaskId = context.TaskId,
            HarnessCommandId = context.HarnessCommandId
        };

        private static void Press(DialogDecision decision, DialogButton? button, string? resultCode)
        {
            decision.Button = button;
            decision.ResultCode = resultCode;
            decision.Record.PressedButton = button;
        }

        private DialogDecision Finish(DialogDecision decision)
        {
            logger.Log(decision.Record);
            return decision;
        }

        // The button must exist in the dump. When both name and id are given they must identify the same button.
        private static DialogButton? ResolveButton(DialogInfo dialog, string? name, string? automationId)
        {
            var hasName = !string.IsNullOrWhiteSpace(name);
            var hasId = !string.IsNullOrWhiteSpace(automationId);
            if (!hasName && !hasId) return null;
            IEnumerable<DialogButton> buttons = dialog.ButtonDetails.Count > 0
                ? dialog.ButtonDetails
                : dialog.Buttons.Select(b => new DialogButton { Name = b });
            return buttons.FirstOrDefault(b =>
                (!hasName || string.Equals(b.Name, name, StringComparison.Ordinal)) &&
                (!hasId || string.Equals(b.AutomationId, automationId, StringComparison.Ordinal)));
        }
    }
}
