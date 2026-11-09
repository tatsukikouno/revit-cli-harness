#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitHarness.Cli.Installation
{
    /// <summary>
    /// Installs and removes the RevitHarness add-in, and the DialogWatcher.
    /// </summary>
    public sealed class RevitInstaller
    {
        private static readonly int[] SupportedRevitVersions = { 2024, 2025, 2026, 2027 };

        private const string AddinManifestResourceName = "RevitHarness.addin";

        private readonly InstallLayout _layout;
        private readonly WatcherControl _watcher;
        private readonly Func<bool> _isRevitRunning;

        public RevitInstaller(InstallLayout layout)
            : this(layout, new WatcherControl(layout), RevitProcess.IsRunning)
        {
        }

        public RevitInstaller(InstallLayout layout, WatcherControl watcher, Func<bool> isRevitRunning)
        {
            _layout = layout;
            _watcher = watcher;
            _isRevitRunning = isRevitRunning;
        }

        private const string RevitRunningMessage =
            "Revit is running. Close Revit before running {0}: Revit holds the add-in files, so the add-in would be left half replaced.";

        /// <summary>Returns the refusal message when another operation holds the lock or Revit is running.</summary>
        private string? Refusal(string operation, out OperationLock? held)
        {
            held = OperationLock.TryAcquire(_layout);
            if (held == null)
                return OperationLock.BusyMessage;
            if (_isRevitRunning())
            {
                held.Dispose();
                held = null;
                return string.Format(RevitRunningMessage, operation);
            }
            return null;
        }

        public bool IsInstalled(int revitVersion)
        {
            return File.Exists(_layout.ManifestPath(revitVersion)) &&
                File.Exists(Path.Combine(_layout.InstallationFolder(revitVersion), "RevitHarness.Addin.dll"));
        }

        public InstallationStatus GetStatus(int revitVersion)
        {
            var status = new InstallationStatus
            {
                RevitVersion = revitVersion,
                AddinsFolder = _layout.AddinsFolder(revitVersion),
                InstallationFolder = _layout.InstallationFolder(revitVersion),
                ManifestPath = _layout.ManifestPath(revitVersion)
            };

            status.AddinsFolderExists = Directory.Exists(status.AddinsFolder);
            status.ManifestExists = File.Exists(status.ManifestPath);
            status.InstallationFolderExists = Directory.Exists(status.InstallationFolder);

            if (status.InstallationFolderExists)
            {
                status.InstalledFiles = Directory.GetFiles(status.InstallationFolder)
                    .Select(Path.GetFileName)
                    .ToList()!;
            }

            status.IsInstalled = IsInstalled(revitVersion);
            if (status.ManifestExists) status.VendorId = AddinManifest.TryReadVendorId(status.ManifestPath);
            return status;
        }

        /// <summary>
        /// Gets all Revit versions that have an addins folder.
        /// </summary>
        public List<int> GetInstalledRevitVersions()
        {
            if (!Directory.Exists(_layout.AddinsRoot))
                return new List<int>();

            return Directory.GetDirectories(_layout.AddinsRoot)
                .Select(Path.GetFileName)
                .Where(name => int.TryParse(name, out var year) && year >= 2019 && year <= 2030)
                .Select(name => int.Parse(name!))
                .OrderByDescending(v => v)
                .ToList();
        }

        /// <summary>
        /// Installs the add-in for one Revit version, plus the watcher. Re-running
        /// replaces everything this installs; a running watcher is stopped first.
        /// </summary>
        public InstallResult Install(int revitVersion, string? vendorId = null)
        {
            var result = new InstallResult { RevitVersion = revitVersion };

            if (Array.IndexOf(SupportedRevitVersions, revitVersion) < 0)
            {
                result.Success = false;
                result.Message = $"Revit {revitVersion} is not supported by this CLI build. Supported versions: {string.Join(", ", SupportedRevitVersions)}";
                return result;
            }

            var badVendor = vendorId == null ? null : AddinManifest.CheckVendorId(vendorId);
            if (badVendor != null)
            {
                result.Success = false;
                result.Message = badVendor;
                return result;
            }

            var refusal = Refusal("install", out var operation);
            if (refusal != null)
            {
                result.Success = false;
                result.Message = refusal;
                return result;
            }

            using var held = operation;
            try
            {
                var addinsFolder = _layout.AddinsFolder(revitVersion);
                var installFolder = _layout.InstallationFolder(revitVersion);
                var manifestPath = _layout.ManifestPath(revitVersion);

                if (!Directory.Exists(addinsFolder))
                {
                    result.Success = false;
                    result.Message = $"Revit {revitVersion} addins folder does not exist: {addinsFolder}";
                    return result;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var prefix = revitVersion + "/";
                var versionedResourceNames = Array.FindAll(
                    assembly.GetManifestResourceNames(),
                    name => name.StartsWith(prefix, StringComparison.Ordinal));

                if (versionedResourceNames.Length == 0)
                {
                    result.Success = false;
                    result.Message = $"No embedded add-in files found for Revit {revitVersion} in this CLI build.";
                    return result;
                }

                if (!File.Exists(Path.Combine(_layout.BundledWatcherDir, InstallLayout.WatcherExeName)))
                {
                    result.Success = false;
                    result.Message = $"This CLI build does not bundle the watcher (expected {Path.Combine(_layout.BundledWatcherDir, InstallLayout.WatcherExeName)}).";
                    return result;
                }

                var stopped = _watcher.StopLocked();
                if (!stopped.Ok)
                {
                    result.Success = false;
                    result.Message = $"Cannot replace the watcher: {stopped.Message}";
                    return result;
                }

                // A reinstall keeps the VendorId already installed unless one is given.
                try { vendorId ??= File.Exists(manifestPath) ? AddinManifest.ReadVendorId(manifestPath) : null; }
                catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException)
                {
                    result.Success = false;
                    result.Message = $"The installed {manifestPath} cannot be read ({ex.Message}); pass --vendor-id to replace it.";
                    return result;
                }

                var extractedFiles = new List<string>();

                ReplaceDirectory(installFolder);
                foreach (var resourceName in versionedResourceNames)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        continue;

                    var destPath = Path.Combine(installFolder, resourceName.Substring(prefix.Length));
                    using var fileStream = File.Create(destPath);
                    stream.CopyTo(fileStream);
                    extractedFiles.Add(destPath);
                }

                using (var manifestStream = assembly.GetManifestResourceStream(AddinManifestResourceName))
                {
                    if (manifestStream != null)
                    {
                        AddinManifest.Write(manifestStream, manifestPath, vendorId);
                        extractedFiles.Add(manifestPath);
                    }
                }

                ReplaceDirectory(_layout.WatcherDir);
                CopyDirectory(_layout.BundledWatcherDir, _layout.WatcherDir, extractedFiles);

                result.Success = true;
                result.Message = $"RevitHarness installed successfully for Revit {revitVersion}" +
                    (stopped.Stopped ? ". The dialog watcher was running and has been stopped; run 'revit-harness dialog-watcher start' before launching Revit." : "");
                result.InstalledFiles = extractedFiles;
                result.InstallationFolder = installFolder;
                result.VendorId = AddinManifest.TryReadVendorId(manifestPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Installation failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Removes the add-in for one Revit version. The watcher is shared by all
        /// versions, so they go with the last installed version. The dialogs data folder stays.
        /// </summary>
        public UninstallResult Uninstall(int revitVersion)
        {
            var result = new UninstallResult { RevitVersion = revitVersion };

            var refusal = Refusal("uninstall", out var operation);
            if (refusal != null)
            {
                result.Success = false;
                result.Message = refusal;
                return result;
            }

            using var held = operation;
            try
            {
                var removedFiles = new List<string>();

                var manifestPath = _layout.ManifestPath(revitVersion);
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                    removedFiles.Add(manifestPath);
                }

                var installFolder = _layout.InstallationFolder(revitVersion);
                if (Directory.Exists(installFolder))
                {
                    Directory.Delete(installFolder, recursive: true);
                    removedFiles.Add(installFolder);
                }

                if (!SupportedRevitVersions.Any(IsInstalled))
                {
                    var stopped = _watcher.StopLocked();
                    if (!stopped.Ok)
                    {
                        result.Success = false;
                        result.Message = $"Cannot remove the watcher: {stopped.Message}";
                        return result;
                    }

                    if (Directory.Exists(_layout.WatcherDir))
                    {
                        Directory.Delete(_layout.WatcherDir, recursive: true);
                        removedFiles.Add(_layout.WatcherDir);
                    }
                }

                result.Success = true;
                result.Message = removedFiles.Count == 0
                    ? $"RevitHarness was not installed for Revit {revitVersion}"
                    : $"RevitHarness uninstalled successfully from Revit {revitVersion}";
                result.RemovedFiles = removedFiles;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Uninstallation failed: {ex.Message}";
            }

            return result;
        }

        internal static void ReplaceDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            Directory.CreateDirectory(path);
        }

        internal static void CopyDirectory(string source, string dest, List<string> copied)
        {
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dest, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                copied.Add(target);
            }
        }
    }

    /// <summary>
    /// Installation status information.
    /// </summary>
    public class InstallationStatus
    {
        public int RevitVersion { get; set; }
        public string AddinsFolder { get; set; } = string.Empty;
        public string InstallationFolder { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public bool AddinsFolderExists { get; set; }
        public bool InstallationFolderExists { get; set; }
        public bool ManifestExists { get; set; }
        public bool IsInstalled { get; set; }
        public List<string> InstalledFiles { get; set; } = new List<string>();
        public string? VendorId { get; set; }
    }

    /// <summary>
    /// Installation result.
    /// </summary>
    public class InstallResult
    {
        public int RevitVersion { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string InstallationFolder { get; set; } = string.Empty;
        public List<string> InstalledFiles { get; set; } = new List<string>();
        public string? VendorId { get; set; }
    }

    /// <summary>
    /// Uninstallation result.
    /// </summary>
    public class UninstallResult
    {
        public int RevitVersion { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> RemovedFiles { get; set; } = new List<string>();
    }
}
