using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    public static class Reversibility
    {
        private static readonly HashSet<string> SafeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ok", "cancel", "close", "閉じる", "キャンセル"
        };

        private static readonly HashSet<string> SafeAutomationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IDOK", "IDCANCEL", "IDCLOSE", "1", "2", "8"
        };

        private static readonly HashSet<string> SafeResultCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ok", "Cancel", "Close", "TaskDialogResult.Ok", "TaskDialogResult.Cancel", "TaskDialogResult.Close",
            "IDOK", "IDCANCEL", "IDCLOSE"
        };

        public static bool IsReversible(DialogButton? button) => button != null &&
            IsReversible(button.Name, button.AutomationId, null);

        public static bool IsReversible(DialogAction? action) => action != null &&
            IsReversible(action.ButtonName, action.ButtonAutomationId, action.ResultCode);

        // Every identifier that is present must be in its allow set. Anything else is treated as irreversible.
        private static bool IsReversible(string? name, string? automationId, string? resultCode)
        {
            var any = false;
            if (!string.IsNullOrWhiteSpace(name)) { any = true; if (!SafeNames.Contains(NormalizeName(name!))) return false; }
            if (!string.IsNullOrWhiteSpace(automationId)) { any = true; if (!SafeAutomationIds.Contains(NormalizeAutomationId(automationId!))) return false; }
            if (!string.IsNullOrWhiteSpace(resultCode)) { any = true; if (!SafeResultCodes.Contains(resultCode!.Trim())) return false; }
            return any;
        }

        // Revit TaskDialog standard buttons report "CommandButton_<id>" (for example CommandButton_8 for Close).
        private static string NormalizeAutomationId(string automationId)
        {
            var id = automationId.Trim();
            return id.StartsWith("CommandButton_", StringComparison.OrdinalIgnoreCase) ? id.Substring("CommandButton_".Length) : id;
        }

        // Drops mnemonics such as "&OK" and trailing accelerator hints such as "OK(O)".
        private static string NormalizeName(string name) =>
            Regex.Replace(name.Replace("&", string.Empty), @"\s*\(.\)\s*$", string.Empty).Trim();
    }
}
