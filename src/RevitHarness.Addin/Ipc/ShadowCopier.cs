#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// Copies target DLLs and their dependencies without locking build output.
    /// </summary>
    public class ShadowCopier
    {
        private const int MaxGenerations = 10;
        private const int CleanupAgeDays = 1;

        private readonly string _shadowRoot;
        private readonly Action<string> _log;

        public ShadowCopier(Action<string> log)
        {
            _log = log;
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _shadowRoot = Path.Combine(localAppData, "RevitHarness", "shadow");
        }

        /// <summary>
        /// Copies the assembly and its dependencies to a timestamped shadow directory.
        /// </summary>
        /// <param name="assemblyPath">Path to the main assembly.</param>
        /// <returns>Path to the shadow copied assembly.</returns>
        public string CopyToShadow(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly not found: {assemblyPath}", assemblyPath);

            // Create timestamped directory
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
            var shadowDir = Path.Combine(_shadowRoot, timestamp);
            Directory.CreateDirectory(shadowDir);

            _log($"Shadow copy directory: {shadowDir}");

            // Copy main assembly
            var sourceDir = Path.GetDirectoryName(assemblyPath);
            var assemblyName = Path.GetFileName(assemblyPath);
            var shadowAssemblyPath = Path.Combine(shadowDir, assemblyName);

            File.Copy(assemblyPath, shadowAssemblyPath, overwrite: true);
            _log($"Copied: {assemblyName}");

            // Copy dependent DLLs from the same directory
            if (sourceDir != null)
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName != assemblyName)
                    {
                        var destPath = Path.Combine(shadowDir, fileName);
                        File.Copy(file, destPath, overwrite: true);
                        _log($"Copied dependency: {fileName}");
                    }
                }

                // Copy PDB files for debugging
                foreach (var file in Directory.GetFiles(sourceDir, "*.pdb"))
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(shadowDir, fileName);
                    File.Copy(file, destPath, overwrite: true);
                    _log($"Copied debug symbols: {fileName}");
                }

                // Copy config files
                foreach (var file in Directory.GetFiles(sourceDir, "*.config"))
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(shadowDir, fileName);
                    File.Copy(file, destPath, overwrite: true);
                    _log($"Copied config: {fileName}");
                }
            }

            return shadowAssemblyPath;
        }

        /// <summary>
        /// Cleans up old shadow directories.
        /// </summary>
        public void Cleanup()
        {
            try
            {
                if (!Directory.Exists(_shadowRoot))
                    return;

                var directories = Directory.GetDirectories(_shadowRoot)
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.CreationTime)
                    .ToList();

                var live = new HashSet<string>(Generation.LiveShadowDirectories, StringComparer.OrdinalIgnoreCase);
                var cutoffDate = DateTime.Now.AddDays(-CleanupAgeDays);
                var toDelete = directories
                    .Skip(MaxGenerations)
                    .Where(d => d.CreationTime < cutoffDate && !live.Contains(d.FullName))
                    .ToList();

                foreach (var dir in toDelete)
                {
                    try
                    {
                        dir.Delete(recursive: true);
                        _log($"Cleaned up old shadow directory: {dir.Name}");
                    }
                    catch (Exception ex)
                    {
                        _log($"Failed to delete shadow directory {dir.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"Cleanup error: {ex.Message}");
            }
        }
    }
}
