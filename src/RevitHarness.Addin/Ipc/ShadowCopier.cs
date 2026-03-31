#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// Handles shadow copying and byte-array loading of DLLs to avoid file locks
    /// and assembly caching issues.
    /// </summary>
    public class ShadowCopier
    {
        private const int MaxGenerations = 10;
        private const int CleanupAgeDays = 1;

        private readonly string _shadowRoot;
        private readonly Action<string> _log;
        private readonly ConcurrentDictionary<string, Assembly> _pinnedAssemblies = new ConcurrentDictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public ShadowCopier(Action<string> log)
        {
            _log = log;
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _shadowRoot = Path.Combine(localAppData, "RevitHarness", "shadow");
        }

        /// <summary>
        /// Pins an assembly so that the resolver always returns it instead of searching the AppDomain.
        /// Use this for infrastructure assemblies (e.g., RevitHarness.Contracts) to prevent conflicts
        /// when other addins (e.g., RevitSDK AddIn Manager) load duplicate copies.
        /// </summary>
        public void PinAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            if (name != null)
            {
                _pinnedAssemblies.TryAdd(name, assembly);
                _log($"Pinned assembly: {name} -> {assembly.FullName}");
            }
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
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
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
        /// Loads an assembly from bytes to bypass the .NET assembly identity cache.
        /// This allows reloading modified assemblies with the same name+version without restarting Revit.
        /// </summary>
        /// <param name="assemblyPath">Path to the main assembly (already shadow-copied).</param>
        /// <returns>The loaded assembly and a resolver delegate that should be attached to AssemblyResolve.</returns>
        public (Assembly assembly, ResolveEventHandler resolver) LoadAssemblyFromBytes(string assemblyPath)
        {
            var shadowDir = Path.GetDirectoryName(assemblyPath)!;
            var loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

            // Load main assembly from bytes
            var assemblyBytes = File.ReadAllBytes(assemblyPath);
            byte[]? pdbBytes = null;

            // Try to load PDB for debugging support
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                pdbBytes = File.ReadAllBytes(pdbPath);
                _log($"Loading with debug symbols: {Path.GetFileName(pdbPath)}");
            }

            var assembly = pdbBytes != null
                ? Assembly.Load(assemblyBytes, pdbBytes)
                : Assembly.Load(assemblyBytes);

            _log($"Loaded from bytes: {assembly.FullName}");
            loadedAssemblies[assembly.GetName().Name!] = assembly;

            // Create resolver for dependencies in the shadow directory
            ResolveEventHandler resolver = (sender, args) =>
            {
                var requestedName = new AssemblyName(args.Name).Name;
                if (requestedName == null) return null;

                // Check if already loaded by us
                if (loadedAssemblies.TryGetValue(requestedName, out var cached))
                {
                    _log($"Resolved from cache: {requestedName}");
                    return cached;
                }

                // Check pinned assemblies first. These are infrastructure assemblies (e.g., RevitHarness.Contracts)
                // that must always resolve to the Harness addin's own copy, even when other addins
                // (e.g., RevitSDK AddIn Manager) have loaded duplicate copies into the AppDomain.
                if (_pinnedAssemblies.TryGetValue(requestedName, out var pinned))
                {
                    _log($"Resolved from pinned: {requestedName} -> {pinned.FullName}");
                    loadedAssemblies[requestedName] = pinned;
                    return pinned;
                }

                // Check if already loaded in AppDomain.
                // This prevents loading a duplicate from shadow bytes which would create
                // a different type identity, causing IsAssignableFrom checks to fail.
                var existing = Array.Find(
                    AppDomain.CurrentDomain.GetAssemblies(),
                    a => string.Equals(a.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    _log($"Resolved from AppDomain: {requestedName} -> {existing.FullName}");
                    loadedAssemblies[requestedName] = existing;
                    return existing;
                }

                // Try to load from shadow directory
                var dllPath = Path.Combine(shadowDir, requestedName + ".dll");
                if (File.Exists(dllPath))
                {
                    _log($"Resolving dependency from shadow: {requestedName}");
                    var depBytes = File.ReadAllBytes(dllPath);
                    byte[]? depPdbBytes = null;

                    var depPdbPath = Path.ChangeExtension(dllPath, ".pdb");
                    if (File.Exists(depPdbPath))
                    {
                        depPdbBytes = File.ReadAllBytes(depPdbPath);
                    }

                    var depAssembly = depPdbBytes != null
                        ? Assembly.Load(depBytes, depPdbBytes)
                        : Assembly.Load(depBytes);

                    loadedAssemblies[requestedName] = depAssembly;
                    _log($"Loaded dependency from bytes: {depAssembly.FullName}");
                    return depAssembly;
                }

                return null; // Let default resolution handle it
            };

            return (assembly, resolver);
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

                var cutoffDate = DateTime.Now.AddDays(-CleanupAgeDays);
                var toDelete = directories
                    .Skip(MaxGenerations)
                    .Where(d => d.CreationTime < cutoffDate)
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
