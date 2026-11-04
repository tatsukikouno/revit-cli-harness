using System;
using System.Collections.Generic;
using System.IO;
using RevitHarness.Dialogs;

namespace RevitHarness.DialogWatcher
{
    public sealed class WatcherOptions
    {
        public bool Once { get; set; }
        public bool DryRun { get; set; }
        public bool SelfTest { get; set; }
        public int? Pid { get; set; }
        public string? AllowlistPath { get; set; }
        // Treated like LOCALAPPDATA: records land in <LogDir>\RevitHarness\dialogs.
        public string? LogDir { get; set; }
        public ISet<int> ExtraPids { get; } = new HashSet<int>();
        public int PollMilliseconds { get; set; } = 300;

        public string DialogsDirectory => DialogPaths.GetDirectory(LogDir);
        public string DumpsDirectory => Path.Combine(DialogsDirectory, "dumps");
        public string ScreenshotsDirectory => Path.Combine(DialogsDirectory, "screenshots");
        public string StopFilePath => Path.Combine(DialogsDirectory, "watcher.stop");
        public string PidFilePath => Path.Combine(DialogsDirectory, "watcher.pid");

        public static WatcherOptions Parse(string[] args)
        {
            var o = new WatcherOptions();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--once": o.Once = true; break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--selftest": o.SelfTest = true; break;
                    case "--pid":
                        if (!int.TryParse(Next(args, ref i), out var pid)) throw new ArgumentException("--pid needs an integer");
                        o.Pid = pid;
                        break;
                    case "--allowlist": o.AllowlistPath = Path.GetFullPath(Next(args, ref i)); break;
                    case "--log-dir": o.LogDir = Path.GetFullPath(Next(args, ref i)); break;
                    default: throw new ArgumentException("unknown argument: " + args[i]);
                }
            }
            return o;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }
    }
}
