using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaUI.Core.AutomationElements;

namespace RevitHarness.DialogWatcher
{
    public static class UiaDump
    {
        public const int MaxDepth = 8;
        public const int MaxNodes = 500;

        public static string? Write(AutomationElement window, string directory, string name)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var budget = MaxNodes;
                var root = Node(window, 0, ref budget);
                var path = Path.Combine(directory, name + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
                return path;
            }
            catch { return null; }
        }

        private static Dictionary<string, object?> Node(AutomationElement e, int depth, ref int budget)
        {
            budget--;
            var node = new Dictionary<string, object?>
            {
                ["controlType"] = Safe(() => e.ControlType.ToString()),
                ["name"] = Safe(() => e.Name),
                ["automationId"] = Safe(() => e.AutomationId),
                ["className"] = Safe(() => e.ClassName),
                ["isEnabled"] = Safe(() => (object?)e.Properties.IsEnabled.ValueOrDefault),
                ["invokable"] = Safe(() => (object?)e.Patterns.Invoke.IsSupported)
            };
            if (depth >= MaxDepth) { node["truncated"] = "depth"; return node; }
            var children = new List<object>();
            AutomationElement[] kids = Array.Empty<AutomationElement>();
            try { kids = e.FindAllChildren(); } catch { /* element vanished */ }
            foreach (var kid in kids)
            {
                if (budget <= 0) { node["truncated"] = "nodes"; break; }
                children.Add(Node(kid, depth + 1, ref budget));
            }
            if (children.Count > 0) node["children"] = children;
            return node;
        }

        private static object? Safe(Func<object?> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
