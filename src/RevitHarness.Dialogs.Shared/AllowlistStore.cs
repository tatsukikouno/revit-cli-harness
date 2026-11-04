using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RevitHarness.Dialogs
{
    public static class AllowlistStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { WriteIndented = true };

        public static bool TryAddRule(AllowRule rule, string? baseDirectory = null) =>
            Mutate(baseDirectory, rules =>
            {
                if (rules.Any(r => r.Id == rule.Id)) return false;
                rules.Add(rule);
                return true;
            });

        public static bool TryRemoveRule(string ruleId, string? baseDirectory = null) =>
            Mutate(baseDirectory, rules => rules.RemoveAll(r => r.Id == ruleId) > 0);

        public static bool TryUpdateRule(string ruleId, Action<AllowRule> edit, string? baseDirectory = null) =>
            Mutate(baseDirectory, rules =>
            {
                var rule = rules.FirstOrDefault(r => r.Id == ruleId);
                if (rule == null) return false;
                edit(rule);
                return true;
            });

        // Read-modify-write under a cross-process lock, published through a temp file so readers never see a partial file.
        // An unreadable allowlist is never overwritten.
        private static bool Mutate(string? baseDirectory, Func<List<AllowRule>, bool> change)
        {
            try
            {
                var path = DialogPaths.GetAllowlistPath(baseDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var gate = CrossProcessLock.Acquire(path, TimeSpan.FromSeconds(10));
                if (!gate.Held) return false;
                var current = AllowlistLoader.Load(baseDirectory);
                if (current.Error != null) return false;
                var rules = current.Rules.ToList();
                if (!change(rules)) return false;
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, JsonSerializer.Serialize(new AllowlistFile { Rules = rules }, WriteOptions),
                        new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return true;
            }
            catch { return false; }
        }
    }
}
