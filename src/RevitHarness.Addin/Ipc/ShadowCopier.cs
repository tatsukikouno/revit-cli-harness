#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// Copies a target's output directory without locking the build output.
    /// </summary>
    public class ShadowCopier
    {
        // A shadow directory is a full copy of the target's output, so only the newest few non-live ones are kept for diagnosis.
        private const int KeepNewest = 3;

        private readonly string _shadowRoot;
        private readonly Action<string> _log;

        // The root is a parameter so tests do not write into the real shadow folder.
        public ShadowCopier(Action<string> log, string? shadowRoot = null)
        {
            _log = log;
            _shadowRoot = shadowRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitHarness", "shadow");
        }

        /// <summary>
        /// Copies the whole directory that holds the assembly (every file, subdirectories included) to a
        /// timestamped shadow directory, so the generation loads by path and Assembly.Location and relative
        /// files beside the DLL work unmodified.
        /// </summary>
        /// <param name="assemblyPath">Path to the main assembly.</param>
        /// <returns>Path to the shadow copied assembly.</returns>
        public string CopyToShadow(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly not found: {assemblyPath}", assemblyPath);

            var sourceDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
            var shadowDir = Path.Combine(_shadowRoot, timestamp);
            _log($"Shadow copy directory: {shadowDir}");
            var count = CopyTree(sourceDir, shadowDir);
            _log($"Copied {count} files from {sourceDir}");
            return Path.Combine(shadowDir, Path.GetFileName(assemblyPath));
        }

        private static int CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            var count = 0;
            foreach (var file in Directory.GetFiles(source))
            {
                var copy = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, copy, overwrite: true);
                // The copy belongs to the shadow: a read-only source file must not block the net48 rewrite or the later cleanup.
                File.SetAttributes(copy, FileAttributes.Normal);
                count++;
            }
            foreach (var directory in Directory.GetDirectories(source))
                count += CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
            return count;
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
                var toDelete = directories
                    .Skip(KeepNewest)
                    .Where(d => !live.Contains(d.FullName))
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
