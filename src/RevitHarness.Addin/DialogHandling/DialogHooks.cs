#nullable enable

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitHarness.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitHarness.Addin.DialogHandling
{
    /// <summary>
    /// Fixed-in-add-in handlers for DialogBoxShowing and FailuresProcessing. Never throws into Revit.
    /// </summary>
    public sealed class DialogHooks
    {
        private readonly Action<string> _log;
        private readonly DialogLogger _logger = new DialogLogger();
        private UIControlledApplication? _uiApp;
        private ControlledApplication? _app;

        public DialogHooks(Action<string> log) => _log = log;

        public void Subscribe(UIControlledApplication application)
        {
            _uiApp = application;
            _app = application.ControlledApplication;
            DialogRuntime.SetSession(_app.VersionNumber, null);
            _uiApp.DialogBoxShowing += OnDialogBoxShowing;
            _app.FailuresProcessing += OnFailuresProcessing;
        }

        public void Unsubscribe()
        {
            try { if (_uiApp != null) _uiApp.DialogBoxShowing -= OnDialogBoxShowing; } catch { }
            try { if (_app != null) _app.FailuresProcessing -= OnFailuresProcessing; } catch { }
            _uiApp = null;
            _app = null;
        }

        private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
        {
            try
            {
                DialogInfo info;
                switch (e)
                {
                    case TaskDialogShowingEventArgs task:
                        info = DialogInfoBuilder.ForTaskDialog(task.DialogId, task.Message);
                        break;
                    case MessageBoxShowingEventArgs box:
                        info = DialogInfoBuilder.ForMessageBox(box.DialogId, box.Message, box.DialogType);
                        break;
                    default:
                        info = DialogInfoBuilder.ForTaskDialog(e.DialogId, null);
                        info.Title = "DialogBox";
                        break;
                }

                var decider = new DialogDecider(DialogRuntime.NewContext());

                string? screenshot = null;
                var known = DialogMatcher.FindDecisionRule(AllowlistLoader.Load().Rules, info, DialogSource.Api) is { NoLearn: false };
                if (!known) screenshot = RevitWindowCapture.Capture(DialogPaths.GetDirectory(), Guid.NewGuid().ToString("N").Substring(0, 8));

                var decision = decider.Decide(info, DialogSource.Api, screenshot);
                if (decision.ShouldPress && DialogInfoBuilder.TryGetResultCode(decision, out var code) && DialogInfoBuilder.IsOffered(info, code))
                    e.OverrideResult(code);
            }
            catch (Exception ex)
            {
                _log($"DialogBoxShowing handler error: {ex.Message}");
            }
        }

        private void OnFailuresProcessing(object? sender, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
        {
            try
            {
                var accessor = e.GetFailuresAccessor();
                var rules = AllowlistLoader.Load().Rules;
                var context = DialogRuntime.NewContext();
                foreach (var message in accessor.GetFailureMessages().ToList())
                {
                    var severity = message.GetSeverity();
                    var info = DialogInfoBuilder.ForFailure(message.GetFailureDefinitionId().Guid, message.GetDescriptionText());
                    var record = new DialogLogRecord
                    {
                        Source = DialogSource.Api,
                        Dialog = info,
                        Key = DialogKey.From(info),
                        Pid = context.Pid,
                        RevitVersion = context.RevitVersion,
                        OpenDocument = context.OpenDocument,
                        TaskId = context.TaskId,
                        HarnessCommandId = context.HarnessCommandId,
                        DecidedBy = "rule",
                        MatchedRuleId = "unknown",
                        ActionTaken = "none",
                        Note = "failure severity: " + severity
                    };
                    if (severity == FailureSeverity.Warning)
                    {
                        // Warnings are matched by failure id only, so a text-only dialog rule never deletes a warning.
                        var rule = DialogMatcher.FindWarningDeleteRule(rules, info);
                        if (rule != null)
                        {
                            accessor.DeleteWarning(message);
                            record.MatchedRuleId = rule.Id;
                            record.ActionTaken = "deleteWarning";
                        }
                    }
                    _logger.Log(record);
                }
            }
            catch (Exception ex)
            {
                _log($"FailuresProcessing handler error: {ex.Message}");
            }
        }
    }
}
