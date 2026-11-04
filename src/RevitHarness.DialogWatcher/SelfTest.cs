using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using RevitHarness.Dialogs;

namespace RevitHarness.DialogWatcher
{
    // Shows a fake WinForms dialog in this process and runs the watcher against it, once without a rule
    // (expects dump + screenshot + log, nothing pressed) and once with a rule (expects a press and a close).
    public static class SelfTest
    {
        private const string Title = "RevitHarness Selftest";

        public static int Run(WatcherOptions baseOptions)
        {
            var root = baseOptions.LogDir ?? Path.Combine(Path.GetTempPath(), "rhw-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var ruleFile = Path.Combine(root, "selftest-allowlist.json");
            var failures = 0;
            void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

            Form? form = null;
            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                form = new Form { Text = Title, Width = 360, Height = 160, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
                form.Controls.Add(new Label { Text = "Selftest body: do you want to continue?", AutoSize = true, Left = 12, Top = 12, Name = "lblBody" });
                var ok = new Button { Text = "OK", Name = "btnOk", Left = 12, Top = 60, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Name = "btnCancel", Left = 110, Top = 60, DialogResult = DialogResult.Cancel };
                ok.Click += (s, e) => form!.Close();
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.Shown += (s, e) => ready.Set();
                Application.Run(form);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!ready.Wait(10000)) { Console.WriteLine("FAIL fake dialog did not appear"); return 1; }

            WatcherOptions Make() => new WatcherOptions { LogDir = root, AllowlistPath = ruleFile, Once = true };
            var pid = Environment.ProcessId;

            Directory.CreateDirectory(root);
            File.WriteAllText(ruleFile, "{\"version\":1,\"rules\":[]}");
            var a = Make();
            a.ExtraPids.Add(pid);
            using (var w = new DialogWatcher(a)) w.ScanOnce();
            Check(form != null && form.Visible, "unknown dialog left open (nothing pressed)");
            Check(Directory.Exists(a.DumpsDirectory) && Directory.GetFiles(a.DumpsDirectory, "*.json").Length > 0, "UIA dump written");
            Check(Directory.Exists(a.ScreenshotsDirectory) && Directory.GetFiles(a.ScreenshotsDirectory, "*.png").Length > 0, "screenshot written");

            var rule = "{\"version\":1,\"rules\":[{\"id\":\"selftest\",\"target\":\"watcher\",\"match\":{\"title\":{\"value\":\"" + Title +
                       "\",\"mode\":\"equals\"}},\"action\":{\"buttonName\":\"OK\"}}]}";
            File.WriteAllText(ruleFile, rule);
            var b = Make();
            b.ExtraPids.Add(pid);
            using (var w = new DialogWatcher(b)) w.ScanOnce();
            Thread.Sleep(300);
            Check(form != null && !form.Visible, "rule-matched dialog closed via Invoke");

            var lines = Directory.GetFiles(b.DialogsDirectory, "dialogs_*.jsonl").SelectMany(File.ReadAllLines).ToList();
            Check(lines.Any(l => l.Contains("\"matchedRuleId\":\"unknown\"") && l.Contains("dumpPath\":\"")), "log has unknown record with dumpPath");
            Check(lines.Any(l => l.Contains("\"kind\":\"outcome\"") && l.Contains("\"closed\":true")), "log has outcome closed=true");
            Console.WriteLine("selftest output: " + b.DialogsDirectory);
            return failures == 0 ? 0 : 1;
        }
    }
}
