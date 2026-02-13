#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitHarness.Cli.Installation
{
    /// <summary>
    /// Handles installation and uninstallation of the RevitHarness add-in to Revit.
    /// </summary>
    public static class RevitInstaller
    {
        private static readonly string[] EmbeddedResources = new[]
        {
            "RevitHarness.Addin.dll",
            "RevitHarness.Contracts.dll",
            "RevitHarness.Lookup.dll",
            "LookupEngine.dll",
            "LookupEngine.Abstractions.dll",
            "System.Text.Json.dll",
            "System.Text.Encodings.Web.dll",
            "System.Memory.dll",
            "System.Buffers.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "System.Threading.Tasks.Extensions.dll",
            "System.Numerics.Vectors.dll",
            "System.IO.Pipelines.dll",
            "Newtonsoft.Json.dll",
            "RevitHarness.addin"
        };

        /// <summary>
        /// Gets the Revit addins folder path for a specific version.
        /// </summary>
        public static string GetAddinsFolder(int revitVersion)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Autodesk", "Revit", "Addins", revitVersion.ToString());
        }

        /// <summary>
        /// Gets the RevitHarness installation folder within the addins folder.
        /// </summary>
        public static string GetInstallationFolder(int revitVersion)
        {
            return Path.Combine(GetAddinsFolder(revitVersion), "RevitHarness");
        }

        /// <summary>
        /// Gets the path to the .addin manifest file.
        /// </summary>
        public static string GetAddinManifestPath(int revitVersion)
        {
            return Path.Combine(GetAddinsFolder(revitVersion), "RevitHarness.addin");
        }

        /// <summary>
        /// Checks if RevitHarness is installed for a specific Revit version.
        /// </summary>
        public static bool IsInstalled(int revitVersion)
        {
            var manifestPath = GetAddinManifestPath(revitVersion);
            var installFolder = GetInstallationFolder(revitVersion);
            var addinDll = Path.Combine(installFolder, "RevitHarness.Addin.dll");

            return File.Exists(manifestPath) && File.Exists(addinDll);
        }

        /// <summary>
        /// Gets installation status information for a specific Revit version.
        /// </summary>
        public static InstallationStatus GetStatus(int revitVersion)
        {
            var status = new InstallationStatus
            {
                RevitVersion = revitVersion,
                AddinsFolder = GetAddinsFolder(revitVersion),
                InstallationFolder = GetInstallationFolder(revitVersion),
                ManifestPath = GetAddinManifestPath(revitVersion)
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

            status.IsInstalled = status.ManifestExists &&
                status.InstallationFolderExists &&
                File.Exists(Path.Combine(status.InstallationFolder, "RevitHarness.Addin.dll"));

            return status;
        }

        /// <summary>
        /// Gets all supported Revit versions that have an addins folder.
        /// </summary>
        public static List<int> GetInstalledRevitVersions()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var revitFolder = Path.Combine(appData, "Autodesk", "Revit", "Addins");

            if (!Directory.Exists(revitFolder))
                return new List<int>();

            return Directory.GetDirectories(revitFolder)
                .Select(Path.GetFileName)
                .Where(name => int.TryParse(name, out var year) && year >= 2019 && year <= 2030)
                .Select(name => int.Parse(name!))
                .OrderByDescending(v => v)
                .ToList();
        }

        /// <summary>
        /// Installs the RevitHarness add-in for a specific Revit version.
        /// </summary>
        public static InstallResult Install(int revitVersion, bool force = false)
        {
            var result = new InstallResult { RevitVersion = revitVersion };

            try
            {
                var addinsFolder = GetAddinsFolder(revitVersion);
                var installFolder = GetInstallationFolder(revitVersion);
                var manifestPath = GetAddinManifestPath(revitVersion);

                // Check if addins folder exists
                if (!Directory.Exists(addinsFolder))
                {
                    result.Success = false;
                    result.Message = $"Revit {revitVersion} addins folder does not exist: {addinsFolder}";
                    return result;
                }

                // Check if already installed
                if (IsInstalled(revitVersion) && !force)
                {
                    result.Success = false;
                    result.Message = $"RevitHarness is already installed for Revit {revitVersion}. Use --force to reinstall.";
                    return result;
                }

                // Create installation folder
                Directory.CreateDirectory(installFolder);

                var assembly = Assembly.GetExecutingAssembly();
                var extractedFiles = new List<string>();

                foreach (var resourceName in EmbeddedResources)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                    {
                        // Resource might not be embedded yet (first build)
                        Console.Error.WriteLine($"Warning: Resource '{resourceName}' not found in assembly");
                        continue;
                    }

                    string destPath;
                    if (resourceName.EndsWith(".addin", StringComparison.OrdinalIgnoreCase))
                    {
                        destPath = manifestPath;
                    }
                    else
                    {
                        destPath = Path.Combine(installFolder, resourceName);
                    }

                    using var fileStream = File.Create(destPath);
                    stream.CopyTo(fileStream);
                    extractedFiles.Add(destPath);
                }

                result.Success = true;
                result.Message = $"RevitHarness installed successfully for Revit {revitVersion}";
                result.InstalledFiles = extractedFiles;
                result.InstallationFolder = installFolder;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Installation failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Uninstalls the RevitHarness add-in for a specific Revit version.
        /// </summary>
        public static UninstallResult Uninstall(int revitVersion)
        {
            var result = new UninstallResult { RevitVersion = revitVersion };

            try
            {
                var installFolder = GetInstallationFolder(revitVersion);
                var manifestPath = GetAddinManifestPath(revitVersion);

                var removedFiles = new List<string>();

                // Remove manifest
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                    removedFiles.Add(manifestPath);
                }

                // Remove installation folder
                if (Directory.Exists(installFolder))
                {
                    var files = Directory.GetFiles(installFolder);
                    foreach (var file in files)
                    {
                        File.Delete(file);
                        removedFiles.Add(file);
                    }
                    Directory.Delete(installFolder);
                    removedFiles.Add(installFolder);
                }

                if (removedFiles.Count == 0)
                {
                    result.Success = true;
                    result.Message = $"RevitHarness was not installed for Revit {revitVersion}";
                }
                else
                {
                    result.Success = true;
                    result.Message = $"RevitHarness uninstalled successfully from Revit {revitVersion}";
                    result.RemovedFiles = removedFiles;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Uninstallation failed: {ex.Message}";
            }

            return result;
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
