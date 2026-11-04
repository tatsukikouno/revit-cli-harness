#nullable enable
#if NET48
using System;
using System.IO;
using System.Reflection;

namespace RevitHarness.Addin
{
    /// <summary>
    /// Revit 2024 (net48) has no binding redirects for add-ins, so a harness dependency compiled against 8.0.0.0 fails to load
    /// when the bundled file is 8.0.0.6. Loads the same-named file from the add-in folder, but only for requests made by
    /// harness assemblies or their bundled dependencies and only when the bundled file is not older than the request.
    /// Revit 2025 and later (net8.0) bind these from the runtime and the add-in folder without help.
    /// </summary>
    internal static class BundledDependencyResolver
    {
        private static readonly string Directory = Path.GetDirectoryName(typeof(BundledDependencyResolver).Assembly.Location) ?? string.Empty;
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            _installed = false;
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        private static Assembly? Resolve(object? sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            var name = requested.Name;
            if (name == null || !name.StartsWith("System.", StringComparison.Ordinal) && !name.StartsWith("Microsoft.Bcl.", StringComparison.Ordinal)) return null;
            if (!IsHarnessRequester(args.RequestingAssembly)) return null;
            var path = Path.Combine(Directory, name + ".dll");
            if (!File.Exists(path)) return null;
            var bundled = AssemblyName.GetAssemblyName(path);
            if (requested.Version != null && bundled.Version < requested.Version) return null;
            return Assembly.LoadFrom(path);
        }

        private static bool IsHarnessRequester(Assembly? requester)
        {
            if (requester == null) return false;
            var name = requester.GetName().Name ?? string.Empty;
            if (name.StartsWith("RevitHarness.", StringComparison.Ordinal)) return true;
            try
            {
                var location = requester.IsDynamic ? string.Empty : requester.Location;
                return location.Length > 0 &&
                    string.Equals(Path.GetDirectoryName(location), Directory, StringComparison.OrdinalIgnoreCase);
            }
            catch (NotSupportedException) { return false; }
        }
    }
}
#endif
