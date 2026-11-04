using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using RevitHarness.Dialogs;

namespace RevitHarness.DialogWatcher
{
    public static class DialogCollector
    {
        private static readonly HashSet<string> TitleBarButtons = new HashSet<string> { "Minimize-Restore", "Maximize-Restore", "Close" };

        public static DialogInfo Collect(AutomationElement window, string processName)
        {
            var info = new DialogInfo
            {
                Source = DialogSource.Watcher,
                Title = Safe(() => window.Name),
                AutomationId = Safe(() => window.AutomationId),
                ProcessName = processName
            };
            info.DialogId = info.AutomationId;
            var texts = new List<string>();
            foreach (var e in Descendants(window))
            {
                var type = Safe(() => e.ControlType.ToString());
                if (IsButton(e))
                {
                    var button = new DialogButton { Name = Safe(() => e.Name), AutomationId = Safe(() => e.AutomationId) };
                    if (button.AutomationId != null && TitleBarButtons.Contains(button.AutomationId)) continue;
                    info.ButtonDetails.Add(button);
                    info.Buttons.Add(button.Name ?? button.AutomationId ?? string.Empty);
                }
                else if (type == ControlType.Text.ToString())
                {
                    var t = Safe(() => e.Name);
                    if (!string.IsNullOrWhiteSpace(t)) texts.Add(t!.Trim());
                }
                else if (type == ControlType.Edit.ToString() || type == ControlType.Document.ToString())
                {
                    var t = Safe(() => e.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault) ?? Safe(() => e.Name);
                    if (!string.IsNullOrWhiteSpace(t)) texts.Add(t!.Trim());
                }
            }
            info.Text = string.Join("\n", texts);
            return info;
        }

        public static bool IsButton(AutomationElement e) =>
            Safe(() => e.ControlType.ToString()) == ControlType.Button.ToString() ||
            Safe(() => e.ClassName) == "CCPushButton";

        public static IEnumerable<AutomationElement> Descendants(AutomationElement window)
        {
            try { return window.FindAllDescendants(); }
            catch { return Array.Empty<AutomationElement>(); }
        }

        public static AutomationElement? FindButton(AutomationElement window, DialogButton button) =>
            Descendants(window).FirstOrDefault(e =>
                IsButton(e) &&
                Safe(() => e.Name) == button.Name &&
                (string.IsNullOrEmpty(button.AutomationId) || Safe(() => e.AutomationId) == button.AutomationId));

        private static string? Safe(Func<string?> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
