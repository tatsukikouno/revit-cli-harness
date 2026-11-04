using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    public static class DialogMatcher
    {
        // The rule that decides a dialog. A matching noLearn rule always wins over an earlier action rule,
        // so a tester's noLearn marker cannot be overridden by rule order.
        public static AllowRule? FindDecisionRule(IEnumerable<AllowRule> rules, DialogInfo dialog, DialogSource source)
        {
            var list = rules as IReadOnlyList<AllowRule> ?? rules.ToList();
            return FindFirstMatch(list.Where(r => r != null && r.NoLearn), dialog, source) ?? FindFirstMatch(list, dialog, source);
        }

        // A Revit warning is deleted only by an enabled rule with a dialogId condition that is not marked noLearn,
        // and never when a noLearn rule also matches it.
        public static AllowRule? FindWarningDeleteRule(IEnumerable<AllowRule> rules, DialogInfo failure)
        {
            var rule = FindDecisionRule(rules.Where(r => r?.Match?.DialogId != null), failure, DialogSource.Api);
            return rule != null && !rule.NoLearn ? rule : null;
        }

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
                if (match.AddIn != null && (!unsigned || !AddInMatches(match.AddIn, dialog))) continue;
                if (!Matches(match.AutomationId, dialog.AutomationId)) continue;
                if (Matches(match.DialogId, dialog.DialogId) && Matches(match.Title, dialog.Title) &&
                    Matches(match.Text, dialog.Text) && Matches(match.ProcessName, dialog.ProcessName)) return rule;
            }
            return null;
        }

        // The security dialog may only be answered by a rule that names the add-in; any other rule is ignored for it.
        public static bool IsAddInRule(AllowRule rule) => rule.Match?.AddIn != null;

        // Only evaluated for the unsigned add-in dialog. Ordinal, case-insensitive. A trailing '*' makes the rest a prefix;
        // a lone '*' matches every add-in. A pattern containing a path separator is compared with the dll path only,
        // any other pattern with the add-in name and dll file name, so a name cannot impersonate a path.
        private static bool AddInMatches(string pattern, DialogInfo dialog)
        {
            if (pattern == "*") return true;
            var prefix = pattern.EndsWith("*", StringComparison.Ordinal);
            var text = prefix ? pattern.Substring(0, pattern.Length - 1) : pattern;
            var identity = AddInInfo.Identify(dialog);
            var candidates = text.IndexOfAny(new[] { '\\', '/' }) >= 0 ? identity.Paths : identity.NamesAndFiles;
            foreach (var value in candidates)
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
            if (value == null || string.IsNullOrEmpty(pattern.Value)) return false;
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
