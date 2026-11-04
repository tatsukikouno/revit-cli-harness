#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RevitHarness.Cli.Installation
{
    /// <summary>
    /// Cross-process mutual exclusion for install, uninstall and watcher start/stop. An exclusive
    /// file handle is released by the OS when the process ends, so a crash never leaves it held.
    /// </summary>
    public sealed class OperationLock : IDisposable
    {
        public const string BusyMessage =
            "Another revit-harness install, uninstall or dialog-watcher start/stop is already running. Wait for it to finish, then run this command again.";

        private readonly FileStream _handle;

        private OperationLock(FileStream handle) => _handle = handle;

        public static OperationLock? TryAcquire(InstallLayout layout)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(layout.LockFile)!);
            try
            {
                return new OperationLock(new FileStream(layout.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Dispose() => _handle.Dispose();
    }

    public static class RevitProcess
    {
        public static bool IsRunning()
        {
            var processes = Process.GetProcessesByName("Revit");
            foreach (var process in processes)
                process.Dispose();
            return processes.Length > 0;
        }
    }

    /// <summary>
    /// The watcher needs the runtime its own runtimeconfig.json names (Microsoft.WindowsDesktop.App).
    /// A runtime of the same major version and at least the requested version satisfies it.
    /// </summary>
    public static class DesktopRuntime
    {
        private const string FrameworkName = "Microsoft.WindowsDesktop.App";

        /// <summary>Returns an error message when the runtime is missing, otherwise null.</summary>
        public static string? MissingMessage(string watcherDir, IReadOnlyList<string> dotnetRoots)
        {
            var configPath = Path.Combine(watcherDir, "RevitHarness.DialogWatcher.runtimeconfig.json");
            if (!File.Exists(configPath))
                return $"{configPath} not found. Run 'revit-harness install' again.";

            var wanted = ReadRequiredVersion(configPath);
            var installed = dotnetRoots
                .Select(root => Path.Combine(root, "shared", FrameworkName))
                .Where(Directory.Exists)
                .SelectMany(Directory.GetDirectories)
                .Select(dir => TryParse(Path.GetFileName(dir)))
                .Where(v => v != null)
                .Select(v => v!);

            if (installed.Any(v => v.Major == wanted.Major && v >= wanted))
                return null;

            return $"The dialog watcher needs the .NET {wanted.Major} Desktop Runtime ({FrameworkName} {wanted} or a later {wanted.Major}.x), " +
                $"which was not found under {string.Join(", ", dotnetRoots)}. Install the \".NET Desktop Runtime {wanted.Major}\" (x64) " +
                $"from https://dotnet.microsoft.com/download/dotnet/{wanted.Major}.0 and run 'revit-harness dialog-watcher start' again.";
        }

        private static Version ReadRequiredVersion(string configPath)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            foreach (var framework in doc.RootElement.GetProperty("runtimeOptions").GetProperty("frameworks").EnumerateArray())
            {
                if (framework.GetProperty("name").GetString() == FrameworkName)
                    return TryParse(framework.GetProperty("version").GetString()!)!;
            }
            throw new InvalidOperationException($"{configPath} does not name {FrameworkName}");
        }

        private static Version? TryParse(string text)
        {
            var core = text.Split('-')[0];
            return Version.TryParse(core, out var version) ? version : null;
        }
    }
}
