#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
#if !NET48
using System.Runtime.Loader;
#endif

namespace RevitHarness.Addin.Ipc
{
    public abstract class Generation : IDisposable
    {
        private static readonly ConcurrentDictionary<string, byte> EverOwned =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private static readonly object RegistryLock = new object();
        private static readonly List<(string Directory, WeakReference Lifetime)> Shadows =
            new List<(string, WeakReference)>();

        protected Assembly? LoadedAssembly;
        public Assembly Assembly => LoadedAssembly ?? throw new ObjectDisposedException(nameof(Generation));
        public string ShadowPath { get; }
        public abstract WeakReference Lifetime { get; }
        public abstract IReadOnlyCollection<Assembly> Assemblies { get; }

        protected Generation(string shadowPath) { ShadowPath = shadowPath; }

        public static IReadOnlyList<string> LiveShadowDirectories
        {
            get
            {
                lock (RegistryLock)
                {
                    Shadows.RemoveAll(s => !s.Lifetime.IsAlive);
                    return Shadows.Select(s => s.Directory).ToArray();
                }
            }
        }

        public static Generation Load(string shadowAssemblyPath, IEnumerable<Assembly> hostAssemblies, Action<string> log)
        {
            shadowAssemblyPath = Path.GetFullPath(shadowAssemblyPath);
            var hosts = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (RegistryLock)
            {
                foreach (var assembly in hostAssemblies)
                    hosts[assembly.GetName().Name!] = assembly;
                var mainName = AssemblyName.GetAssemblyName(shadowAssemblyPath).Name!;
                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var path in Directory.GetFiles(Path.GetDirectoryName(shadowAssemblyPath)!, "*.dll"))
                {
                    AssemblyName shadowName;
                    try { shadowName = AssemblyName.GetAssemblyName(path); }
                    catch (BadImageFormatException) { log($"Skipping native DLL: {path}"); continue; }

                    var name = shadowName.Name!;
                    if (hosts.ContainsKey(name)) continue;
                    // A copy another add-in already loaded is shared only when it is at least as new as the one shipped with the target.
                    var host = name == mainName || EverOwned.ContainsKey(name)
                        ? null
                        : loaded.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)
                            && a.GetName().Version >= shadowName.Version);
                    if (host != null) hosts[name] = host;
                    else owned[name] = path;
                }
                foreach (var name in owned.Keys) EverOwned.TryAdd(name, 0);
            }
#if NET48
            Generation generation = new ByteGeneration(shadowAssemblyPath, owned, hosts, log);
#else
            Generation generation = new CollectibleGeneration(shadowAssemblyPath, owned, hosts);
#endif
            lock (RegistryLock)
                Shadows.Add((Path.GetDirectoryName(shadowAssemblyPath)!, generation.Lifetime));
            log($"Loaded generation: {generation.Assembly.FullName}");
            return generation;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool WaitUntilCollected(WeakReference lifetime)
        {
            for (var i = 0; lifetime.IsAlive && i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            return !lifetime.IsAlive;
        }

        public abstract void Dispose();

#if NET48
        private sealed class ByteGeneration : Generation
        {
            private readonly Dictionary<string, Assembly> _assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, Assembly> _hosts;
            private static readonly List<ByteGeneration> Live = new List<ByteGeneration>();
            private readonly Action<string> _log;
            private static int _stamp;
            private ResolveEventHandler? _resolver;
            public override WeakReference Lifetime { get; }
            public override IReadOnlyCollection<Assembly> Assemblies => _assemblies.Values;

            public ByteGeneration(string path, Dictionary<string, string> owned, Dictionary<string, Assembly> hosts, Action<string> log) : base(path)
            {
                _hosts = hosts;
                _log = log;
                _resolver = Resolve;
                AppDomain.CurrentDomain.AssemblyResolve += _resolver;
                try
                {
                    var mainName = AssemblyName.GetAssemblyName(path).Name!;
                    var suffix = "_g" + System.Threading.Interlocked.Increment(ref _stamp);
                    var renames = owned.Keys
                        .Where(n => !string.Equals(n, mainName, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(n => n, n => n + suffix, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in owned)
                    {
                        var pdbPath = Path.ChangeExtension(item.Value, ".pdb");
                        var (dllBytes, pdbBytes) = renames.Count == 0
                            ? (File.ReadAllBytes(item.Value), File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null)
                            : AssemblyRenamer.Rename(File.ReadAllBytes(item.Value), File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null, renames);
                        var key = renames.TryGetValue(item.Key, out var renamed) ? renamed : item.Key;
                        _assemblies[key] = pdbBytes != null ? Assembly.Load(dllBytes, pdbBytes) : Assembly.Load(dllBytes);
                    }
                    LoadedAssembly = _assemblies[AssemblyName.GetAssemblyName(path).Name!];
                    lock (Live) Live.Add(this);
                    Lifetime = new WeakReference(LoadedAssembly, true);
                }
                catch { Dispose(); throw; }
            }

            private Assembly? Resolve(object? sender, ResolveEventArgs args)
            {
                // Resident generations can have identical identities; only the requester identifies its owner.
                var name = new AssemblyName(args.Name).Name!;
                // Revit 2024 can deliver static binds with a null requester; the newest live generation that owns the name answers those.
                bool mine;
                if (args.RequestingAssembly == null)
                {
                    lock (Live) mine = ReferenceEquals(Live.LastOrDefault(g => g._assemblies.ContainsKey(name)), this);
                }
                else mine = _assemblies.Values.Contains(args.RequestingAssembly);
                _log($"Resolve {name} requester={args.RequestingAssembly?.GetName().Name ?? "null"} mine={mine} main={LoadedAssembly?.GetName().Name}");
                if (!mine) return null;
                if (_assemblies.TryGetValue(name, out var owned)) return owned;
                return _hosts.TryGetValue(name, out var host) ? host : null;
            }

            public override void Dispose()
            {
                lock (Live) Live.Remove(this);
                if (_resolver != null) AppDomain.CurrentDomain.AssemblyResolve -= _resolver;
                _resolver = null;
                LoadedAssembly = null;
                _assemblies.Clear();
                _hosts.Clear();
            }
        }
#else
        private sealed class CollectibleGeneration : Generation
        {
            private GenerationContext? _context;
            public override WeakReference Lifetime { get; }
            public override IReadOnlyCollection<Assembly> Assemblies => _context?.Assemblies.ToArray() ?? Array.Empty<Assembly>();

            public CollectibleGeneration(string path, Dictionary<string, string> owned, Dictionary<string, Assembly> hosts) : base(path)
            {
                _context = new GenerationContext(owned, hosts);
                Lifetime = new WeakReference(_context, true);
                try { LoadedAssembly = _context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(path)); }
                catch { Dispose(); throw; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public override void Dispose()
            {
                LoadedAssembly = null;
                var context = _context;
                _context = null;
                context?.Unload();
            }
        }

        private sealed class GenerationContext : AssemblyLoadContext
        {
            private readonly Dictionary<string, string> _owned;
            // Weak: a resident target's assemblies handed to a throwaway generation must not pin the target's context once it is replaced.
            private readonly Dictionary<string, WeakReference<Assembly>> _hosts;
            private readonly AssemblyLoadContext _hostContext = GetLoadContext(typeof(Generation).Assembly)!;

            public GenerationContext(Dictionary<string, string> owned, Dictionary<string, Assembly> hosts)
                : base("RevitHarness-" + Guid.NewGuid().ToString("N"), isCollectible: true)
            {
                _owned = owned;
                _hosts = hosts.ToDictionary(h => h.Key, h => new WeakReference<Assembly>(h.Value), StringComparer.OrdinalIgnoreCase);
            }

            protected override Assembly? Load(AssemblyName name)
            {
                if (_owned.TryGetValue(name.Name!, out var path))
                {
                    using var dll = new MemoryStream(File.ReadAllBytes(path));
                    var pdbPath = Path.ChangeExtension(path, ".pdb");
                    using var pdb = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
                    return LoadFromStream(dll, pdb);
                }
                if (_hosts.TryGetValue(name.Name!, out var host) && host.TryGetTarget(out var hostAssembly)) return hostAssembly;
                try { return _hostContext.LoadFromAssemblyName(name); }
                catch (FileNotFoundException) { return null; }
                catch (FileLoadException) { return null; }
            }
        }
#endif
    }
}
