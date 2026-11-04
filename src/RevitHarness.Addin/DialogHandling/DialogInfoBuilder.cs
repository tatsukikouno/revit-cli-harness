#nullable enable

using RevitHarness.Dialogs;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitHarness.Addin.DialogHandling
{
    /// <summary>
    /// Builds <see cref="DialogInfo"/> from the few fields Revit's dialog events expose. Free of Revit types so it can be unit tested.
    /// </summary>
    public static class DialogInfoBuilder
    {
        public const string FailureIdPrefix = "failure:";

        public static DialogInfo ForTaskDialog(string? dialogId, string? message) => new DialogInfo
        {
            Source = DialogSource.Api,
            DialogId = dialogId,
            Title = "TaskDialog",
            Text = message,
            ProcessName = "Revit"
        };

        // dialogType is the Win32 MB_* button-set value reported by MessageBoxShowingEventArgs.DialogType.
        public static DialogInfo ForMessageBox(string? dialogId, string? message, int dialogType)
        {
            var info = new DialogInfo
            {
                Source = DialogSource.Api,
                DialogId = dialogId,
                Title = "MessageBox",
                Text = message,
                ProcessName = "Revit"
            };
            foreach (var code in ButtonSetFor(dialogType))
            {
                var name = DialogResultCodes.NameOf(code);
                info.Buttons.Add(name);
                info.ButtonDetails.Add(new DialogButton { Name = name, AutomationId = code.ToString(CultureInfo.InvariantCulture) });
            }
            return info;
        }

        public static DialogInfo ForFailure(Guid definitionId, string? description) => new DialogInfo
        {
            Source = DialogSource.Api,
            DialogId = FailureIdPrefix + definitionId.ToString("D"),
            Title = "Revit warning",
            Text = description,
            ProcessName = "Revit"
        };

        public static IReadOnlyList<int> ButtonSetFor(int dialogType)
        {
            switch (dialogType & 0xF)
            {
                case 0: return new[] { 1 };
                case 1: return new[] { 1, 2 };
                case 2: return new[] { 3, 4, 5 };
                case 3: return new[] { 6, 7, 2 };
                case 4: return new[] { 6, 7 };
                case 5: return new[] { 4, 2 };
                default: return Array.Empty<int>();
            }
        }

        // Result to hand to OverrideResult: a button's AutomationId (MessageBox buttons carry the code) or the rule's resultCode.
        public static bool TryGetResultCode(DialogDecision decision, out int code)
        {
            code = 0;
            if (decision.Button != null && !string.IsNullOrEmpty(decision.Button.AutomationId) &&
                int.TryParse(decision.Button.AutomationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out code)) return true;
            return !string.IsNullOrEmpty(decision.ResultCode) && DialogResultCodes.TryParse(decision.ResultCode!, out code);
        }
    }

    public static class DialogResultCodes
    {
        private static readonly Dictionary<string, int> Named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ok"] = 1, ["Cancel"] = 2, ["Abort"] = 3, ["Retry"] = 4, ["Ignore"] = 5, ["Yes"] = 6, ["No"] = 7, ["Close"] = 8,
            ["CommandLink1"] = 1001, ["CommandLink2"] = 1002, ["CommandLink3"] = 1003, ["CommandLink4"] = 1004
        };

        public static bool TryParse(string value, out int code) =>
            Named.TryGetValue(value.Trim(), out code) ||
            int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);

        public static string NameOf(int code)
        {
            foreach (var pair in Named) if (pair.Value == code) return pair.Key;
            return code.ToString(CultureInfo.InvariantCulture);
        }
    }
}
