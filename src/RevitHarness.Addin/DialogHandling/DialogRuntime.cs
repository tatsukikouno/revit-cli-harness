#nullable enable

using RevitHarness.Dialogs;
using System;
using System.Collections.Generic;

namespace RevitHarness.Addin.DialogHandling
{
    /// <summary>
    /// Command identity of the request currently running on the Revit thread, read by the dialog handlers.
    /// </summary>
    public static class DialogRuntime
    {
        private static readonly object Gate = new object();
        private static string? _commandId;
        private static string? _taskId;
        private static string? _document;
        private static string? _revitVersion;

        public static void SetCommand(string? commandId, string? taskId) { lock (Gate) { _commandId = commandId; _taskId = taskId; } }
        public static void SetSession(string? revitVersion, string? document)
        {
            lock (Gate) { if (revitVersion != null) _revitVersion = revitVersion; _document = document; }
        }

        public static DecisionContext NewContext()
        {
            lock (Gate)
            {
                var context = new DecisionContext { RevitVersion = _revitVersion, OpenDocument = _document };
                if (!string.IsNullOrEmpty(_commandId)) context.HarnessCommandId = _commandId;
                if (!string.IsNullOrEmpty(_taskId)) context.TaskId = _taskId;
                return context;
            }
        }
    }
}
