using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    public static class DialogMatcher
    {
        public static AllowRule? FindFirstMatch(IEnumerable<AllowRule> rules, DialogInfo dialog, DialogSource source)
        {
            DialogKey? key = null;
            var unsigned = AddInInfo.IsUnsignedAddInDialog(dialog);
            foreach (var rule in rules)
            {
                if (rule == null || !rule.Enabled || !Includes(rule.Target, source)) continue;
                if (unsigned && !IsAddInRule(rule)) continue;
                var match = rule.Match;
                if (match == null || (rule.Key == null && match.DialogId == null && match.Title == null &&
                    match.Text == null && match.ProcessName == null && match.AutomationId == null && match.Buttons == null &&
                    match.AddIn == null)) continue;
                if (rule.Key != null && !rule.Key.Equals(key ??= DialogKey.From(dialog))) continue;
                if (match.Buttons != null && !SameButtons(match.Buttons, dialog)) continue;
                if (match.AddIn != null && !AddInMatches(match.AddIn, dialog)) continue;
                if (!Matches(match.AutomationId, dialog.AutomationId)) continue;
                if (Matches(match.DialogId, dialog.DialogId) && Matches(match.Title, dialog.Title) &&
                    Matches(match.Text, dialog.Text) && Matches(match.ProcessName, dialog.ProcessName)) return rule;
            }
            return null;
        }

        // The security dialog may only be answered by a rule that names the add-in; any other rule is ignored for it.
        public static bool IsAddInRule(AllowRule rule) => rule.Match?.AddIn != null;

        // Ordinal, case-insensitive. A trailing '*' makes the rest a prefix; a lone '*' matches every add-in.
        // Checked against every extracted identity: the name, the full dll path and the dll file name.
        private static bool AddInMatches(string pattern, DialogInfo dialog)
        {
            if (pattern == "*") return true;
            var prefix = pattern.EndsWith("*", StringComparison.Ordinal);
            var text = prefix ? pattern.Substring(0, pattern.Length - 1) : pattern;
            foreach (var value in AddInInfo.Extract(dialog))
                if (prefix ? value.StartsWith(text, StringComparison.OrdinalIgnoreCase) : string.Equals(value, text, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool SameButtons(List<string> expected, DialogInfo dialog)
        {
            var actual = dialog.ButtonDetails.Count > 0
                ? dialog.ButtonDetails.Select(b => b.Name ?? string.Empty)
                : dialog.Buttons;
            return expected.OrderBy(b => b, StringComparer.Ordinal).SequenceEqual(actual.OrderBy(b => b, StringComparer.Ordinal), StringComparer.Ordinal);
        }

        private static bool Includes(DialogTarget target, DialogSource source) =>
            (source == DialogSource.Api && (target == DialogTarget.Api || target == DialogTarget.Both)) ||
            (source == DialogSource.Watcher && (target == DialogTarget.Watcher || target == DialogTarget.Both));

        private static bool Matches(TextPattern? pattern, string? value)
        {
            if (pattern == null) return true;
            if (value == null || pattern.Value == null) return false;
            var comparison = pattern.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            switch (pattern.Mode)
            {
                case TextPatternMode.Contains: return value.IndexOf(pattern.Value, comparison) >= 0;
                case TextPatternMode.Equals: return string.Equals(value, pattern.Value, comparison);
                case TextPatternMode.Regex:
                    try
                    {
                        return Regex.IsMatch(value, pattern.Value, RegexOptions.CultureInvariant |
                            (pattern.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromMilliseconds(100));
                    }
                    catch (ArgumentException) { return false; }
                    catch (RegexMatchTimeoutException) { return false; }
                default: return false;
            }
        }
    }
}
