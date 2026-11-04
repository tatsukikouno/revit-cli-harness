#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace RevitHarness.Cli.Installation
{
    public sealed class WatcherState
    {
        public bool Running { get; set; }
        public int? Pid { get; set; }
        public string LogDirectory { get; set; } = string.Empty;
        public string? LatestLog { get; set; }
    }

    public sealed class WatcherResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public WatcherState State { get; set; } = new WatcherState();
    }

    /// <summary>
    /// Starts, stops and inspects the installed DialogWatcher. The watcher owns watcher.pid, so
    /// "running" means that pid is alive and is the watcher process.
    /// </summary>
    public sealed class WatcherControl
    {
        private const string ProcessName = "RevitHarness.DialogWatcher";
        private const int MutexHeldExitCode = 3;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        private readonly InstallLayout _layout;
        private readonly Func<int, string?> _processName;
        private readonly Func<string, int> _spawn;

        public WatcherControl(InstallLayout layout)
            : this(layout, NameOfProcess, SpawnHidden)
        {
        }

        public WatcherControl(InstallLayout layout, Func<int, string?> processName, Func<string, int> spawn)
        {
            _layout = layout;
            _processName = processName;
            _spawn = spawn;
        }

        public WatcherState GetState()
        {
            var pid = ReadRunningPid();
            var latest = Directory.Exists(_layout.DialogsDir)
                ? Directory.GetFiles(_layout.DialogsDir, "dialogs_*.jsonl").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault()
                : null;
            return new WatcherState { Running = pid.HasValue, Pid = pid, LogDirectory = _layout.DialogsDir, LatestLog = latest };
        }

        public WatcherResult Status()
        {
            var state = GetState();
            return new WatcherResult { Ok = state.Running, Message = state.Running ? "running" : "not running", State = state };
        }

        public WatcherResult Start()
        {
            var running = ReadRunningPid();
            if (running.HasValue)
                return Result(true, $"already running (pid {running})");

            if (!File.Exists(_layout.WatcherExe))
                return Result(false, $"watcher is not installed: {_layout.WatcherExe} not found. Run 'revit-harness install' first.");

            int spawned;
            try { spawned = _spawn(_layout.WatcherExe); }
            catch (Exception ex) { return Result(false, $"could not start the watcher: {ex.Message}"); }

            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var pid = ReadRunningPid();
                if (pid.HasValue)
                    return Result(true, pid == spawned ? $"started (pid {pid})" : $"already running (pid {pid})");
                if (_processName(spawned) == null)
                    break;
                Thread.Sleep(100);
            }

            var final = ReadRunningPid();
            if (final.HasValue)
                return Result(true, $"already running (pid {final})");
            return Result(false, $"the watcher did not stay running (exit code {MutexHeldExitCode} means another instance holds the session lock); check {_layout.DialogsDir}");
        }

        public WatcherResult Stop()
        {
            var pid = ReadRunningPid();
            if (!pid.HasValue)
                return Result(true, "not running");

            Directory.CreateDirectory(_layout.DialogsDir);
            File.WriteAllText(_layout.StopFile, "stop");
            try
            {
                var deadline = DateTime.UtcNow + StopTimeout;
                while (DateTime.UtcNow < deadline && ReadRunningPid().HasValue)
                    Thread.Sleep(100);

                var stillRunning = ReadRunningPid();
                if (!stillRunning.HasValue)
                    return Result(true, $"stopped (pid {pid})");

                try
                {
                    using var process = Process.GetProcessById(stillRunning.Value);
                    process.Kill();
                    process.WaitForExit((int)StopTimeout.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    return Result(false, $"could not stop the watcher (pid {stillRunning}): {ex.Message}");
                }
                return Result(true, $"killed after timeout (pid {pid})");
            }
            finally
            {
                try { File.Delete(_layout.StopFile); } catch (IOException) { }
            }
        }

        private int? ReadRunningPid()
        {
            if (!File.Exists(_layout.PidFile))
                return null;
            string text;
            try { text = File.ReadAllText(_layout.PidFile).Trim(); }
            catch (IOException) { return null; }
            if (!int.TryParse(text, out var pid))
                return null;
            return _processName(pid) == ProcessName ? pid : (int?)null;
        }

        private WatcherResult Result(bool ok, string message) =>
            new WatcherResult { Ok = ok, Message = message, State = GetState() };

        private static string? NameOfProcess(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.HasExited ? null : process.ProcessName;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static int SpawnHidden(string exe)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("Process.Start returned no process");
            using (process)
                return process.Id;
        }
    }
}
