#nullable enable

using System;
using System.IO;
using System.Reflection;

namespace RevitHarness.Addin
{
    /// <summary>
    /// Revit has no binding redirects for add-ins, so a dependency compiled against 8.0.0.0 fails to load
    /// when the bundled file is 8.0.0.6. Loads the same-named file from the add-in folder instead.
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
            var name = new AssemblyName(args.Name).Name;
            if (name == null || !name.StartsWith("System.", StringComparison.Ordinal) && !name.StartsWith("Microsoft.Bcl.", StringComparison.Ordinal)) return null;
            var path = Path.Combine(Directory, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
