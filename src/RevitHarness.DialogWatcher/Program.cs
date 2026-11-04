using System;
using System.IO;
using System.Threading;
using RevitHarness.Dialogs;

namespace RevitHarness.DialogWatcher
{
    public static class Program
    {
        private const string MutexName = @"Global\RevitHarness.DialogWatcher";

        public static int Main(string[] args)
        {
            WatcherOptions options;
            try { options = WatcherOptions.Parse(args); }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: RevitHarness.DialogWatcher [--once] [--dry-run] [--pid <n>] [--allowlist <path>] [--log-dir <dir>] [--selftest]");
                return 2;
            }

            if (options.SelfTest) return SelfTest.Run(options);

            using var mutex = new Mutex(false, MutexName);
            bool owned;
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) { Console.Error.WriteLine("another DialogWatcher is already running"); return 3; }

            try
            {
                Directory.CreateDirectory(options.DialogsDirectory);
                if (File.Exists(options.StopFilePath)) File.Delete(options.StopFilePath);
                File.WriteAllText(options.PidFilePath, Environment.ProcessId.ToString());

                using var cancel = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancel.Cancel(); };
                using var watcher = new DialogWatcher(options);
                Console.WriteLine("DialogWatcher started" + (options.DryRun ? " (dry-run)" : "") +
                                  ", logs: " + options.DialogsDirectory);
                if (options.Once) watcher.ScanOnce();
                else watcher.Run(cancel.Token);
                Console.WriteLine("DialogWatcher stopped, dialogs handled: " + watcher.Processed);
                return 0;
            }
            finally
            {
                try { File.Delete(options.PidFilePath); } catch { }
                mutex.ReleaseMutex();
            }
        }
    }
}
