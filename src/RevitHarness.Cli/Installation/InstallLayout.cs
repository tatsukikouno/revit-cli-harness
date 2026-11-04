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

        public InstallLayout(string appData, string localAppData, string skillsDir, string bundleRoot, string[] dotnetRoots)
        {
            AppData = appData;
            LocalAppData = localAppData;
            SkillsDir = skillsDir;
            BundleRoot = bundleRoot;
            DotnetRoots = dotnetRoots;
        }

        public string AppData { get; }
        public string LocalAppData { get; }
        public string SkillsDir { get; }
        public string BundleRoot { get; }
        public string[] DotnetRoots { get; }

        public static InstallLayout Create(string? skillsDir)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new InstallLayout(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                skillsDir ?? Path.Combine(profile, ".claude", "skills"),
                AppContext.BaseDirectory,
                new[] { Environment.GetEnvironmentVariable("DOTNET_ROOT"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet") }
                    .Where(root => !string.IsNullOrEmpty(root)).ToArray()!);
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
