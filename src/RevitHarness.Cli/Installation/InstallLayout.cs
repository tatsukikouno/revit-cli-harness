#nullable enable

using System;
using System.IO;
using System.Linq;
using RevitHarness.Dialogs;

namespace RevitHarness.Cli.Installation
{
    /// <summary>
    /// Every location install, uninstall and the watcher commands read or write. Tests build one
    /// from temp directories so nothing touches the real profile.
    /// </summary>
    public sealed class InstallLayout
    {
        public const string WatcherExeName = "RevitHarness.DialogWatcher.exe";

        public InstallLayout(string appData, string localAppData, string bundleRoot, string[] dotnetRoots)
        {
            AppData = appData;
            LocalAppData = localAppData;
            BundleRoot = bundleRoot;
            DotnetRoots = dotnetRoots;
        }

        public string AppData { get; }
        public string LocalAppData { get; }
        public string BundleRoot { get; }
        public string[] DotnetRoots { get; }

        public static InstallLayout Create()
        {
            return new InstallLayout(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppContext.BaseDirectory,
                DotnetRootCandidates(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"), Environment.GetEnvironmentVariable("DOTNET_ROOT"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        }

        /// <summary>
        /// The one .NET root the apphost loads the runtime from: DOTNET_ROOT_X64, else DOTNET_ROOT,
        /// else the default Program Files install. When an environment variable names a root the host
        /// uses only that root, so the watcher pre-start check looks only there too.
        /// </summary>
        public static string[] DotnetRootCandidates(string? dotnetRootX64, string? dotnetRoot, string programFiles)
        {
            foreach (var fromEnvironment in new[] { dotnetRootX64, dotnetRoot })
            {
                if (!string.IsNullOrWhiteSpace(fromEnvironment))
                    return new[] { fromEnvironment!.Trim() };
            }
            return new[] { Path.Combine(programFiles, "dotnet") };
        }

        public string AddinsRoot => Path.Combine(AppData, "Autodesk", "Revit", "Addins");
        public string AddinsFolder(int revitVersion) => Path.Combine(AddinsRoot, revitVersion.ToString());
        public string InstallationFolder(int revitVersion) => Path.Combine(AddinsFolder(revitVersion), "RevitHarness");
        public string ManifestPath(int revitVersion) => Path.Combine(AddinsFolder(revitVersion), "RevitHarness.addin");

        public string WatcherDir => Path.Combine(LocalAppData, "RevitHarness", "watcher");
        public string WatcherExe => Path.Combine(WatcherDir, WatcherExeName);
        public string DialogsDir => DialogPaths.GetDirectory(LocalAppData);
        public string PidFile => Path.Combine(DialogsDir, "watcher.pid");
        public string LockFile => Path.Combine(LocalAppData, "RevitHarness", "operation.lock");
        public string StopFile => Path.Combine(DialogsDir, "watcher.stop");

        public string BundledWatcherDir => Path.Combine(BundleRoot, "watcher");
        public string BundledSkillsDir => Path.Combine(BundleRoot, "skills");
    }
}
