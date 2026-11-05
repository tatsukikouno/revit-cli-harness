#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections;
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

        public static Generation Load(string shadowAssemblyPath, IEnumerable<Assembly> hostAssemblies, string? revitDirectory, Action<string> log)
        {
            shadowAssemblyPath = Path.GetFullPath(shadowAssemblyPath);
            ReferencePlan plan;
            lock (RegistryLock)
            {
                plan = ReferenceResolver.Plan(shadowAssemblyPath, hostAssemblies, revitDirectory, AppDomain.CurrentDomain.GetAssemblies(), EverOwned.ContainsKey, log);
                foreach (var name in plan.Owned.Keys) EverOwned.TryAdd(name, 0);
            }
#if NET48
            Generation generation = new PathGeneration(shadowAssemblyPath, plan, log);
#else
            WpfAssemblyNameCache.Log = log;
            Generation generation = new CollectibleGeneration(shadowAssemblyPath, plan);
#endif
            // A byte-loaded assembly would not need its files, a path-loaded one maps them until it is collected.
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
#if !NET48
                WpfAssemblyNameCache.Evict();
#endif
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            return !lifetime.IsAlive;
        }

        public abstract void Dispose();

#if NET48
        private sealed class PathGeneration : Generation
        {
            private readonly Dictionary<string, Assembly> _assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, Assembly> _hosts;
            private readonly Dictionary<string, string> _revitFiles;
            private static readonly List<PathGeneration> Live = new List<PathGeneration>();
            private readonly Action<string> _log;
            private static int _stamp;
            private ResolveEventHandler? _resolver;
            public override WeakReference Lifetime { get; }
            public override IReadOnlyCollection<Assembly> Assemblies => _assemblies.Values;

            public PathGeneration(string path, ReferencePlan plan, Action<string> log) : base(path)
            {
                _hosts = plan.Hosts;
                _revitFiles = plan.RevitFiles;
                _log = log;
                _resolver = Resolve;
                AppDomain.CurrentDomain.AssemblyResolve += _resolver;
                try
                {
                    var mainName = AssemblyName.GetAssemblyName(path).Name!;
                    var suffix = "_g" + System.Threading.Interlocked.Increment(ref _stamp);
                    var renames = plan.Owned.Keys
                        .Where(n => !string.Equals(n, mainName, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(n => n, n => n + suffix, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in plan.Owned)
                    {
                        if (renames.Count > 0) RewriteInPlace(item.Value, renames);
                        var key = renames.TryGetValue(item.Key, out var renamed) ? renamed : item.Key;
                        // LoadFile, unlike LoadFrom, loads a second copy of an identity from another path, and Location stays real.
                        _assemblies[key] = Assembly.LoadFile(item.Value);
                    }
                    LoadedAssembly = _assemblies[mainName];
                    lock (Live) Live.Add(this);
                    Lifetime = new WeakReference(LoadedAssembly, true);
                }
                catch { Dispose(); throw; }
            }

            // The shadow directory belongs to this generation, so the rewritten helper replaces its own copy.
            private static void RewriteInPlace(string dllPath, IReadOnlyDictionary<string, string> renames)
            {
                var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                var (dll, pdb) = AssemblyRenamer.Rename(File.ReadAllBytes(dllPath), File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null, renames);
                File.WriteAllBytes(dllPath, dll);
                if (pdb != null) File.WriteAllBytes(pdbPath, pdb);
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
                if (!mine) return null;
                _log($"Resolve {name} requester={args.RequestingAssembly?.GetName().Name ?? "null"} main={LoadedAssembly?.GetName().Name}");
                if (_assemblies.TryGetValue(name, out var owned)) return owned;
                if (_hosts.TryGetValue(name, out var host)) return host;
                return _revitFiles.TryGetValue(name, out var revitFile) ? Assembly.LoadFrom(revitFile) : null;
            }

            public override void Dispose()
            {
                lock (Live) Live.Remove(this);
                if (_resolver != null) AppDomain.CurrentDomain.AssemblyResolve -= _resolver;
                _resolver = null;
                LoadedAssembly = null;
                _assemblies.Clear();
                _hosts.Clear();
                _revitFiles.Clear();
            }
        }
#else
        private sealed class CollectibleGeneration : Generation
        {
            private GenerationContext? _context;
            public override WeakReference Lifetime { get; }
            public override IReadOnlyCollection<Assembly> Assemblies => _context?.Assemblies.ToArray() ?? Array.Empty<Assembly>();

            public CollectibleGeneration(string path, ReferencePlan plan) : base(path)
            {
                _context = new GenerationContext(plan);
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
                if (context == null) return;
                context.Disposed = true;
                context.Unload();
                WpfAssemblyNameCache.Evict();
            }
        }

        // WPF resolves pack URIs and XAML namespaces by walking AppDomain assemblies and memoizes each Assembly -> AssemblyName
        // in static dictionaries that are never pruned. Any such lookup while a generation is live pins its collectible context,
        // and a lookup between Unload and collection (the assembly is still enumerable) pins it again, so every sweep drops
        // all entries that belong to a disposed generation.
        internal static class WpfAssemblyNameCache
        {
            internal readonly struct Cache
            {
                public Cache(IDictionary entries, object gate) { Entries = entries; Gate = gate; }
                public IDictionary Entries { get; }
                public object Gate { get; }
            }

            private static readonly (string Assembly, string Type)[] Owners =
            {
                ("WindowsBase", "MS.Internal.WindowsBase.SafeSecurityHelper"),
                ("PresentationCore", "MS.Internal.PresentationCore.SafeSecurityHelper"),
                ("System.Xaml", "System.Xaml.SafeSecurityHelper")
            };
            private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

            internal static Func<IEnumerable<Cache>> Source { get; set; } = () => Discover(Owners, Log);
            internal static Action<string>? Log { get; set; }
            private static readonly HashSet<string> Reported = new HashSet<string>();

            internal static void Evict()
            {
                foreach (var cache in Source())
                {
                    lock (cache.Gate)
                    {
                        var stale = cache.Entries.Keys.Cast<object>()
                            .Where(k => k is Assembly a && AssemblyLoadContext.GetLoadContext(a) is GenerationContext { Disposed: true })
                            .ToArray();
                        foreach (var key in stale) cache.Entries.Remove(key);
                    }
                }
            }

            // A WPF owner assembly that is loaded but lacks the expected type or fields means WPF changed its internals:
            // eviction would silently do nothing, so say so once per owner.
            internal static IReadOnlyList<Cache> Discover(IEnumerable<(string Assembly, string Type)> owners, Action<string>? log)
            {
                var caches = new List<Cache>();
                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var (assemblyName, typeName) in owners)
                {
                    var assembly = loaded.FirstOrDefault(a => a.GetName().Name == assemblyName);
                    if (assembly == null) continue;
                    var type = assembly.GetType(typeName);
                    var entriesField = type?.GetField("_assemblies", AnyStatic);
                    var gateField = type?.GetField("syncObject", AnyStatic);
                    if (entriesField == null || gateField == null)
                    {
                        if (Reported.Add(typeName))
                            log?.Invoke($"WPF internals changed: {typeName} in {assemblyName} has no static _assemblies/syncObject. Disposed generations are no longer evicted from WPF's assembly-name cache and may stay loaded.");
                        continue;
                    }
                    // WPF creates the dictionary on its first lookup; until then there is nothing to evict.
                    if (!(entriesField.GetValue(null) is IDictionary entries) || gateField.GetValue(null) is not { } gate) continue;
                    caches.Add(new Cache(entries, gate));
                }
                return caches;
            }
        }

        private sealed class GenerationContext : AssemblyLoadContext
        {
            private readonly Dictionary<string, string> _owned;
            private readonly Dictionary<string, string> _revitFiles;
            // Weak: a resident target's assemblies handed to a throwaway generation must not pin the target's context once it is replaced.
            private readonly Dictionary<string, WeakReference<Assembly>> _hosts;
            private readonly AssemblyLoadContext _hostContext = GetLoadContext(typeof(Generation).Assembly)!;
            public bool Disposed { get; set; }

            public GenerationContext(ReferencePlan plan)
                : base("RevitHarness-" + Guid.NewGuid().ToString("N"), isCollectible: true)
            {
                _owned = plan.Owned;
                _revitFiles = plan.RevitFiles;
                _hosts = plan.Hosts.ToDictionary(h => h.Key, h => new WeakReference<Assembly>(h.Value), StringComparer.OrdinalIgnoreCase);
            }

            protected override Assembly? Load(AssemblyName name)
            {
                if (_owned.TryGetValue(name.Name!, out var path)) return LoadFromAssemblyPath(path);
                if (_hosts.TryGetValue(name.Name!, out var host) && host.TryGetTarget(out var hostAssembly)) return hostAssembly;
                try { return _hostContext.LoadFromAssemblyName(name); }
                catch (FileNotFoundException) { }
                catch (FileLoadException) { }
                return _revitFiles.TryGetValue(name.Name!, out var revitFile) ? _hostContext.LoadFromAssemblyPath(revitFile) : null;
            }
        }
#endif
    }
}
