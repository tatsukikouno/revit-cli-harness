#nullable enable

using Autodesk.Revit.UI;
using RevitHarness.Addin.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace RevitHarness.Addin.Commands
{
    public class DiscoveredType
    {
        public string FullName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Namespace { get; set; }
        public string InterfaceType { get; set; } = string.Empty;
    }

    public class AssemblyScanner
    {
        private const string HarnessCommandInterfaceName = "RevitHarness.Contracts.IHarnessCommand";
        private const string ExternalCommandInterfaceName = "Autodesk.Revit.UI.IExternalCommand";

        private readonly ShadowCopier _shadowCopier;
        private readonly Action<string> _log;

        public AssemblyScanner(ShadowCopier shadowCopier, Action<string> log)
        {
            _shadowCopier = shadowCopier;
            _log = log;
        }

        public List<DiscoveredType> Scan(string assemblyPath)
        {
            var results = new List<DiscoveredType>();
            ResolveEventHandler? resolver = null;

            try
            {
                if (!File.Exists(assemblyPath))
                    throw new FileNotFoundException($"Assembly not found: {assemblyPath}");

                _log($"Scanning: {assemblyPath}");
                var shadowPath = _shadowCopier.CopyToShadow(assemblyPath);
                var (assembly, assemblyResolver) = _shadowCopier.LoadAssemblyFromBytes(shadowPath);
                resolver = assemblyResolver;
                AppDomain.CurrentDomain.AssemblyResolve += resolver;

                Type[] exportedTypes;
                try
                {
                    exportedTypes = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    _log($"ReflectionTypeLoadException: some types could not be loaded");
                    exportedTypes = Array.FindAll(ex.Types, t => t != null)!;
                }

                foreach (var type in exportedTypes)
                {
                    if (type == null || type.IsAbstract || type.IsInterface)
                        continue;

                    // Use name-based interface checks to handle assembly identity mismatches
                    // when RevitHarness.Contracts is loaded in multiple contexts
                    // (e.g., both by the Harness addin and by RevitSDK AddIn Manager).
                    if (ReflectionHelper.ImplementsInterfaceByName(type, HarnessCommandInterfaceName))
                    {
                        results.Add(new DiscoveredType
                        {
                            FullName = type.FullName ?? type.Name,
                            Name = type.Name,
                            Namespace = type.Namespace,
                            InterfaceType = "IHarnessCommand"
                        });
                    }
                    else if (ReflectionHelper.ImplementsInterfaceByName(type, ExternalCommandInterfaceName))
                    {
                        results.Add(new DiscoveredType
                        {
                            FullName = type.FullName ?? type.Name,
                            Name = type.Name,
                            Namespace = type.Namespace,
                            InterfaceType = "IExternalCommand"
                        });
                    }
                }

                _log($"Found {results.Count} command type(s)");
            }
            catch (BadImageFormatException ex)
            {
                _log($"BadImageFormatException: {ex.Message}");
                throw;
            }
            finally
            {
                if (resolver != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= resolver;
                }
            }

            return results;
        }

    }
}
