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

        private string BuildTarget(string tag)
        {
            var dir = Path.Combine(_root, tag);
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
            Generation.Load(path, Array.Empty<Assembly>(), _ => { });

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
            using var other = Generation.Load(BuildTarget("v2"), resident.Assemblies, _ => { });

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
            var main = AssemblyRenamer.Rename(File.ReadAllBytes(BuildTarget("v1")), null, names).Dll;
            var helper = AssemblyRenamer.Rename(File.ReadAllBytes(Path.Combine(_root, "v1", "FxHelper.dll")), null, names).Dll;

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
            var owners = new[] { ("System.Private.CoreLib", "System.Object") };

            var first = Generation.WpfAssemblyNameCache.Discover(owners, logged.Add);
            var second = Generation.WpfAssemblyNameCache.Discover(owners, logged.Add);

            Assert.Empty(first);
            Assert.Empty(second);
            var line = Assert.Single(logged);
            Assert.Contains("WPF internals changed", line);
            Assert.Contains("System.Object", line);
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
