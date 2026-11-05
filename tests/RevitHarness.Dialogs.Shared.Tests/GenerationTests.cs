using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RevitHarness.Addin.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public class GenerationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gen-" + Guid.NewGuid().ToString("N"));
        private static readonly List<object> HostHeld = new List<object>();

        public GenerationTests() { Directory.CreateDirectory(_root); }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static void Compile(string name, string source, string outDir, params string[] references)
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll"))
            };
            refs.AddRange(references.Select(r => MetadataReference.CreateFromFile(r)));
            var compilation = CSharpCompilation.Create(
                name,
                new[] { CSharpSyntaxTree.ParseText(source) },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = compilation.Emit(Path.Combine(outDir, name + ".dll"));
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        }

        private int _built;

        private string BuildTarget(string tag)
        {
            // Generation.Load maps the files it is given, so every build is its own directory.
            var dir = Path.Combine(_root, tag + "-" + _built++);
            Directory.CreateDirectory(dir);
            Compile("FxHelper", "namespace Fx { public static class Helper { public static string Tag => \"H" + tag + "\"; } }", dir);
            Compile(
                "FxTarget",
                "namespace Fx { public static class Probe { public static int Counter; public static string Tag => \"M" + tag + "-\" + Helper.Tag; } }",
                dir,
                Path.Combine(dir, "FxHelper.dll"));
            return Path.Combine(dir, "FxTarget.dll");
        }

        private static string Tag(Generation generation) =>
            (string)generation.Assembly.GetType("Fx.Probe")!.GetProperty("Tag")!.GetValue(null)!;

        private static Generation Load(string path) =>
            Generation.Load(path, Array.Empty<Assembly>(), null, _ => { });

        [Fact]
        public void ReloadSeesChangedTargetAndOwnHelperWhileOldGenerationIsAlive()
        {
            using var first = Load(BuildTarget("v1"));
            using var second = Load(BuildTarget("v2"));

            Assert.Equal("Mv1-Hv1", Tag(first));
            Assert.Equal("Mv2-Hv2", Tag(second));
        }

        [Fact]
        public void EntryAssemblyIsOwnedEvenWhenAResidentAssemblyHasTheSameName()
        {
            using var resident = Load(BuildTarget("v1"));
            using var other = Generation.Load(BuildTarget("v2"), resident.Assemblies, null, _ => { });

            Assert.NotSame(resident.Assembly, other.Assembly);
            Assert.Equal("Mv2-Hv2", Tag(other));
        }

        [Fact]
        public void StaticStateIsIndependentPerGeneration()
        {
            using var first = Load(BuildTarget("v1"));
            using var second = Load(BuildTarget("v1"));
            var field = first.Assembly.GetType("Fx.Probe")!.GetField("Counter")!;
            field.SetValue(null, 42);

            Assert.Equal(0, (int)second.Assembly.GetType("Fx.Probe")!.GetField("Counter")!.GetValue(null)!);
        }

        [Fact]
        public void HostAssembliesAreNotDuplicated()
        {
            var dir = Path.Combine(_root, "host");
            Directory.CreateDirectory(dir);
            Compile("FxHostLib", "namespace Fx { public class HostType { } }", dir);
            var hostAssembly = Assembly.LoadFrom(Path.Combine(dir, "FxHostLib.dll"));
            Compile(
                "FxTarget",
                "namespace Fx { public static class Probe { public static System.Type T => typeof(HostType); } }",
                dir,
                Path.Combine(dir, "FxHostLib.dll"));

            using var generation = Load(Path.Combine(dir, "FxTarget.dll"));
            var type = (Type)generation.Assembly.GetType("Fx.Probe")!.GetProperty("T")!.GetValue(null)!;

            Assert.Same(hostAssembly, type.Assembly);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference LoadUseAndDispose(string path, bool keepTypeOnHost)
        {
            var generation = Load(path);
            var type = generation.Assembly.GetType("Fx.Probe")!;
            Assert.NotNull(Tag(generation));
            if (keepTypeOnHost) HostHeld.Add(type);
            var lifetime = generation.Lifetime;
            generation.Dispose();
            return lifetime;
        }

        [Fact]
        public void DisposedGenerationIsCollected()
        {
            var lifetime = LoadUseAndDispose(BuildTarget("v1"), keepTypeOnHost: false);

            Assert.True(Generation.WaitUntilCollected(lifetime));
        }

        [Fact]
        public void HostReferenceToOldTypeKeepsGenerationAliveUntilReleased()
        {
            var lifetime = LoadUseAndDispose(BuildTarget("v1"), keepTypeOnHost: true);

            Assert.False(Generation.WaitUntilCollected(lifetime));
            HostHeld.Clear();
            Assert.True(Generation.WaitUntilCollected(lifetime));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference LoadCacheAndDispose(string path, Dictionary<object, AssemblyName> cache, bool recacheAfterDispose)
        {
            var generation = Load(path);
            var assemblies = generation.Assemblies.ToArray();
            foreach (var assembly in assemblies) cache[assembly] = assembly.GetName();
            var lifetime = generation.Lifetime;
            generation.Dispose();
            if (recacheAfterDispose)
                foreach (var assembly in assemblies) cache[assembly] = assembly.GetName();
            return lifetime;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AssemblyNameCacheEntriesDoNotKeepDisposedGenerationAlive(bool recacheAfterDispose)
        {
            var cache = new Dictionary<object, AssemblyName>();
            var gate = new object();
            var previous = Generation.WpfAssemblyNameCache.Source;
            Generation.WpfAssemblyNameCache.Source = () => new[] { new Generation.WpfAssemblyNameCache.Cache(cache, gate) };
            try
            {
                var lifetime = LoadCacheAndDispose(BuildTarget("v1"), cache, recacheAfterDispose);

                Assert.True(Generation.WaitUntilCollected(lifetime));
                Assert.Empty(cache);
            }
            finally { Generation.WpfAssemblyNameCache.Source = previous; }
        }

        [Fact]
        public void ShadowDirectoryOfLiveGenerationIsReportedUntilCollected()
        {
            var path = BuildTarget("v1");
            var lifetime = LoadUseAndDispose(path, keepTypeOnHost: false);
            Generation.WaitUntilCollected(lifetime);

            Assert.DoesNotContain(Path.GetDirectoryName(path)!, Generation.LiveShadowDirectories);
            using var generation = Load(path);
            Assert.Contains(Path.GetDirectoryName(path)!, Generation.LiveShadowDirectories);
        }

        [Fact]
        public void RenameChangesHelperNameAndItsReferencesButKeepsTypesWorking()
        {
            var names = new Dictionary<string, string> { ["FxHelper"] = "FxHelper_g9" };
            var target = BuildTarget("v1");
            var main = AssemblyRenamer.Rename(File.ReadAllBytes(target), null, names).Dll;
            var helper = AssemblyRenamer.Rename(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(target)!, "FxHelper.dll")), null, names).Dll;

            Assert.Equal("FxHelper_g9", AssemblyName.GetAssemblyName(WriteTemp(helper, "h.dll")).Name);
            using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(main));
            var reader = pe.GetMetadataReader();
            Assert.Contains(reader.AssemblyReferences, r => reader.GetString(reader.GetAssemblyReference(r).Name) == "FxHelper_g9");
            Assert.DoesNotContain(reader.AssemblyReferences, r => reader.GetString(reader.GetAssemblyReference(r).Name) == "FxHelper");
        }

        [Fact]
        public void RenameRewritesInternalsVisibleToOfRenamedSiblings()
        {
            var dir = Path.Combine(_root, "ivt");
            Directory.CreateDirectory(dir);
            Compile(
                "FxFriend",
                "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"FxHelper\")] [assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Other\")] namespace Fx { public static class F { } }",
                dir,
                Path.Combine(typeof(InternalsVisibleToAttribute).Assembly.Location));
            var names = new Dictionary<string, string> { ["FxHelper"] = "FxHelper_g9", ["Other"] = "Other_g9" };

            var bytes = AssemblyRenamer.Rename(File.ReadAllBytes(Path.Combine(dir, "FxFriend.dll")), null, names).Dll;

            using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(bytes));
            var friends = module.Assembly.CustomAttributes.Where(c => c.AttributeType.Name == "InternalsVisibleToAttribute").Select(c => (string)c.ConstructorArguments[0].Value).ToArray();
            Assert.Equal(new[] { "FxHelper_g9", "Other_g9" }, friends);
        }

        [Fact]
        public void MissingWpfInternalsAreReportedOnceAndYieldNoCache()
        {
            var logged = new List<string>();
            var asm = typeof(FakeWpfHelper).Assembly.GetName().Name!;
            var owners = new[] { (asm, typeof(NoWpfFields).FullName!) };

            var first = Generation.WpfAssemblyNameCache.Discover(owners, logged.Add);
            var second = Generation.WpfAssemblyNameCache.Discover(owners, logged.Add);

            Assert.Empty(first);
            Assert.Empty(second);
            var line = Assert.Single(logged);
            Assert.Contains("WPF internals changed", line);
            Assert.Contains(nameof(NoWpfFields), line);
        }

        [Fact]
        public void WpfCacheNotYetCreatedIsSkippedWithoutWarning()
        {
            var logged = new List<string>();

            var caches = Generation.WpfAssemblyNameCache.Discover(new[] { (typeof(FakeWpfHelper).Assembly.GetName().Name!, typeof(UncreatedWpfCache).FullName!) }, logged.Add);

            Assert.Empty(caches);
            Assert.Empty(logged);
        }

        private static class UncreatedWpfCache
        {
            internal static Dictionary<object, AssemblyName>? _assemblies = null;
            internal static readonly object syncObject = new object();
        }

        [Fact]
        public void WpfCacheIsDiscoveredFromStaticDictionaryAndSyncObject()
        {
            var asm = typeof(FakeWpfHelper).Assembly.GetName().Name!;

            var caches = Generation.WpfAssemblyNameCache.Discover(new[] { (asm, typeof(FakeWpfHelper).FullName!) }, null);

            var cache = Assert.Single(caches);
            Assert.Same(FakeWpfHelper._assemblies, cache.Entries);
            Assert.Same(FakeWpfHelper.syncObject, cache.Gate);
        }

        // A Revit-like layout: "Revit" holds FxRevitLike.dll, and the target dir carries its own (CopyLocal) copy
        // of it next to a helper that references it.
        private (string Target, string RevitDir, string Lib) BuildRevitLikeTarget()
        {
            // Assemblies loaded by name stay in the test host, so every layout gets its own library name.
            var lib = "FxRevitLike" + Guid.NewGuid().ToString("N");
            var revitDir = Path.Combine(_root, "revit-" + _built++);
            Directory.CreateDirectory(revitDir);
            Compile(lib, "namespace Fx { public class ApiType { public static string Where => \"revit\"; } }", revitDir);

            var dir = Path.Combine(_root, "rl-" + _built++);
            Directory.CreateDirectory(dir);
            File.Copy(Path.Combine(revitDir, lib + ".dll"), Path.Combine(dir, lib + ".dll"));
            Compile("FxUsesApi", "namespace Fx { public static class Uses { public static System.Type ApiType => typeof(ApiType); } }", dir,
                Path.Combine(dir, lib + ".dll"));
            Compile("FxApiTarget", "namespace Fx { public static class Probe { public static System.Type ApiType => Uses.ApiType; } }", dir,
                Path.Combine(dir, lib + ".dll"), Path.Combine(dir, "FxUsesApi.dll"));
            return (Path.Combine(dir, "FxApiTarget.dll"), revitDir, lib);
        }

        [Fact]
        public void ResolverNeverOwnsAFileThatSitsInTheRevitDirectoryEvenWhenNothingHasLoadedItYet()
        {
            var (target, revitDir, lib) = BuildRevitLikeTarget();

            var plan = ReferenceResolver.Plan(target, Array.Empty<Assembly>(), revitDir, Array.Empty<Assembly>(), _ => false, _ => { });

            Assert.DoesNotContain(lib, plan.Owned.Keys);
            Assert.Equal(Path.Combine(revitDir, lib + ".dll"), plan.RevitFiles[lib]);
            Assert.Contains("FxUsesApi", plan.Owned.Keys);
            Assert.Contains("FxApiTarget", plan.Owned.Keys);
        }

        [Fact]
        public void ResolverSharesTheLoadedCopyOfARevitFileRegardlessOfVersion()
        {
            var (target, revitDir, lib) = BuildRevitLikeTarget();
            var loaded = Assembly.LoadFile(Path.Combine(revitDir, lib + ".dll"));

            var plan = ReferenceResolver.Plan(target, Array.Empty<Assembly>(), revitDir, new[] { loaded }, _ => false, _ => { });

            Assert.Same(loaded, plan.Hosts[lib]);
            Assert.Empty(plan.RevitFiles);
        }

        [Fact]
        public void ResolverIgnoresAPrivateCopyLoadedFromElsewhereAndKeepsTheRevitFile()
        {
            var (target, revitDir, lib) = BuildRevitLikeTarget();
            var privateCopy = Assembly.LoadFile(Path.Combine(Path.GetDirectoryName(target)!, lib + ".dll"));

            var plan = ReferenceResolver.Plan(target, Array.Empty<Assembly>(), revitDir, new[] { privateCopy }, _ => false, _ => { });

            Assert.DoesNotContain(lib, plan.Hosts.Keys);
            Assert.Equal(Path.Combine(revitDir, lib + ".dll"), plan.RevitFiles[lib]);
        }

        [Fact]
        public void ResolverLetsTheTargetOwnACopyNewerThanTheOneRevitShips()
        {
            var lib = "FxSkew" + Guid.NewGuid().ToString("N");
            var revitDir = Path.Combine(_root, "skew-revit-" + _built++);
            var dir = Path.Combine(_root, "skew-" + _built++);
            Directory.CreateDirectory(revitDir);
            Directory.CreateDirectory(dir);
            Compile(lib, "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] namespace Fx { public class T { } }", revitDir);
            Compile(lib, "[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")] namespace Fx { public class T { } }", dir);
            Compile("FxSkewMain", "namespace Fx { public static class Probe { } }", dir);

            var plan = ReferenceResolver.Plan(Path.Combine(dir, "FxSkewMain.dll"), Array.Empty<Assembly>(), revitDir, Array.Empty<Assembly>(), _ => false, _ => { });

            Assert.Contains(lib, plan.Owned.Keys);
            Assert.DoesNotContain(lib, plan.RevitFiles.Keys);
        }

        [Fact]
        public void ResolverSharesAHostAssemblyThatIsNotBesideTheTarget()
        {
            var (target, _, lib) = BuildRevitLikeTarget();

            var plan = ReferenceResolver.Plan(target, new[] { typeof(Uri).Assembly }, null, Array.Empty<Assembly>(), _ => false, _ => { });

            Assert.Same(typeof(Uri).Assembly, plan.Hosts[typeof(Uri).Assembly.GetName().Name!]);
        }

        [Fact]
        public void ResolverSharesAnotherAddinsCopyOnlyWhenItIsAtLeastAsNewAndWasNeverOwned()
        {
            var (target, _, lib) = BuildRevitLikeTarget();
            var shipped = AssemblyName.GetAssemblyName(Path.Combine(Path.GetDirectoryName(target)!, lib + ".dll")).Version!;
            var loaded = Assembly.LoadFile(Path.Combine(Path.GetDirectoryName(target)!, lib + ".dll"));
            Assert.True(loaded.GetName().Version >= shipped);

            var shared = ReferenceResolver.Plan(target, Array.Empty<Assembly>(), null, new[] { loaded }, _ => false, _ => { });
            var neverShared = ReferenceResolver.Plan(target, Array.Empty<Assembly>(), null, new[] { loaded }, name => name == lib, _ => { });

            Assert.Same(loaded, shared.Hosts[lib]);
            Assert.Contains(lib, neverShared.Owned.Keys);
        }

        [Fact]
        public void GenerationBindsHelpersToTheRevitCopyNotTheTargetCopy()
        {
            var (target, revitDir, lib) = BuildRevitLikeTarget();

            using var generation = Generation.Load(target, Array.Empty<Assembly>(), revitDir, _ => { });
            var api = (Type)generation.Assembly.GetType("Fx.Probe")!.GetProperty("ApiType")!.GetValue(null)!;

            Assert.Equal(Path.Combine(revitDir, lib + ".dll"), api.Assembly.Location);
            Assert.DoesNotContain(generation.Assemblies, a => a.GetName().Name == lib);
        }

        [Fact]
        public void ShadowCopyIsTheWholeDirectoryAndTheGenerationSeesItsOwnLocation()
        {
            var dir = Path.Combine(_root, "full-" + _built++);
            Directory.CreateDirectory(Path.Combine(dir, "data"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"a\":1}");
            File.WriteAllText(Path.Combine(dir, "data", "table.csv"), "x,y");
            Compile(
                "FxLocator",
                "namespace Fx { public static class Probe { public static string Read(string name) => System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Probe).Assembly.Location), name)); } }",
                dir);

            var shadowed = new ShadowCopier(_ => { }, Path.Combine(_root, "shadow")).CopyToShadow(Path.Combine(dir, "FxLocator.dll"));
            using var generation = Load(shadowed);
            var read = generation.Assembly.GetType("Fx.Probe")!.GetMethod("Read")!;

            Assert.Equal(shadowed, generation.Assembly.Location);
            Assert.Equal("{\"a\":1}", (string)read.Invoke(null, new object[] { "settings.json" })!);
            Assert.Equal("x,y", (string)read.Invoke(null, new object[] { Path.Combine("data", "table.csv") })!);
            Assert.NotEqual(Path.GetDirectoryName(shadowed), dir);
        }

        [Fact]
        public void SweepRemovesOnlyTheShadowRootsOfExitedProcesses()
        {
            var all = Path.Combine(_root, "all-shadows");
            foreach (var name in new[] { "111", "222", "legacy_20260101" })
            {
                Directory.CreateDirectory(Path.Combine(all, name));
                File.WriteAllText(Path.Combine(all, name, "x.txt"), "x");
            }

            ShadowCopier.SweepDeadProcesses(all, pid => pid == 111, _ => { });

            Assert.True(Directory.Exists(Path.Combine(all, "111")));
            Assert.False(Directory.Exists(Path.Combine(all, "222")));
            Assert.True(Directory.Exists(Path.Combine(all, "legacy_20260101")));
        }

        [Fact]
        public void ShadowCopyRefusesATargetDirectoryThatContainsTheShadowFolder()
        {
            var dir = Path.Combine(_root, "self-" + _built++);
            Directory.CreateDirectory(dir);
            Compile("FxSelf", "namespace Fx { public static class Probe { } }", dir);
            var copier = new ShadowCopier(_ => { }, Path.Combine(dir, "shadow"));

            Assert.Throws<InvalidOperationException>(() => copier.CopyToShadow(Path.Combine(dir, "FxSelf.dll")));
        }

        [Fact]
        public void CleanupKeepsTheNewestThreeAndEveryLiveGenerationAndRemovesTheRest()
        {
            var dir = Path.Combine(_root, "cl-" + _built++);
            Directory.CreateDirectory(dir);
            Compile("FxClean", "namespace Fx { public static class Probe { } }", dir);
            File.SetAttributes(Path.Combine(dir, "FxClean.dll"), FileAttributes.ReadOnly);
            var copier = new ShadowCopier(_ => { }, Path.Combine(_root, "shadow-cl"));

            var paths = Enumerable.Range(0, 6).Select(_ => copier.CopyToShadow(Path.Combine(dir, "FxClean.dll"))).ToArray();
            using var live = Load(paths[0]);
            copier.Cleanup();

            var left = Directory.GetDirectories(Path.Combine(_root, "shadow-cl")).Select(d => Path.Combine(d, "FxClean.dll")).ToHashSet();
            Assert.Contains(paths[0], left);
            Assert.Equal(4, left.Count);
        }

        private static class NoWpfFields { }

        private static class FakeWpfHelper
        {
            internal static readonly Dictionary<object, AssemblyName> _assemblies = new Dictionary<object, AssemblyName>();
            internal static readonly object syncObject = new object();
        }

        private string WriteTemp(byte[] bytes, string name)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
