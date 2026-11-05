#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// Who answers each assembly name a generation can ask for. Owned names load from the generation's shadow
    /// directory, hosted names are shared with the running process, and Revit names come from the Revit install directory.
    /// </summary>
    internal sealed class ReferencePlan
    {
        public Dictionary<string, string> Owned { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Assembly> Hosts { get; } = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> RevitFiles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class ReferenceResolver
    {
        /// <summary>
        /// Classifies every managed DLL beside the entry assembly. A file with the same name in the Revit install
        /// directory is Revit's and never loaded from the target (a normal .addin load behaves the same way).
        /// </summary>
        public static ReferencePlan Plan(
            string entryPath,
            IEnumerable<Assembly> hostAssemblies,
            string? revitDirectory,
            IReadOnlyCollection<Assembly> loaded,
            Func<string, bool> everOwned,
            Action<string> log)
        {
            var plan = new ReferencePlan();
            var hosts = hostAssemblies.GroupBy(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var mainName = AssemblyName.GetAssemblyName(entryPath).Name!;
            foreach (var host in hosts)
                if (!string.Equals(host.Key, mainName, StringComparison.OrdinalIgnoreCase)) plan.Hosts[host.Key] = host.Value;

            foreach (var path in Directory.GetFiles(Path.GetDirectoryName(entryPath)!, "*.dll"))
            {
                AssemblyName shadowName;
                try { shadowName = AssemblyName.GetAssemblyName(path); }
                catch (BadImageFormatException) { log($"Skipping native DLL: {path}"); continue; }

                var name = shadowName.Name!;
                if (name == mainName)
                {
                    plan.Owned[name] = path;
                    continue;
                }
                if (plan.Hosts.ContainsKey(name)) continue;

                var revitFile = revitDirectory == null ? null : Path.Combine(revitDirectory, name + ".dll");
                if (revitFile != null && File.Exists(revitFile))
                {
                    var running = loaded.FirstOrDefault(a => SameName(a, name));
                    if (running != null) plan.Hosts[name] = running;
                    else plan.RevitFiles[name] = revitFile;
                    continue;
                }

                // A copy another add-in already loaded is shared only when it is at least as new as the one shipped with the target.
                var shared = everOwned(name)
                    ? null
                    : loaded.FirstOrDefault(a => SameName(a, name) && a.GetName().Version >= shadowName.Version);
                if (shared != null) plan.Hosts[name] = shared;
                else plan.Owned[name] = path;
            }
            return plan;
        }

        private static bool SameName(Assembly assembly, string name) =>
            string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase);
    }
}
