using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RevitHarness.Addin.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace RevitHarness.Framework.Tests
{
    // The net48 binder cannot unload anything and reuses any loaded assembly of the same simple name, so these tests
    // run the real thing: helpers renamed per generation and loaded by path with Assembly.LoadFile.
    public class FrameworkGenerationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fw-gen-" + Guid.NewGuid().ToString("N"));
        private int _built;

        public FrameworkGenerationTests() { Directory.CreateDirectory(_root); }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static void Compile(string name, string source, string outDir, params string[] references)
        {
            var refs = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
            refs.AddRange(references.Select(r => MetadataReference.CreateFromFile(r)));
            var result = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) }, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithDeterministic(true)).Emit(Path.Combine(outDir, name + ".dll"));
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        }

        private string NewDir(string prefix)
        {
            var dir = Path.Combine(_root, prefix + "-" + _built++);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Unique names per test class instance: the net48 AppDomain keeps every loaded assembly for the whole run.
        private readonly string _id = Guid.NewGuid().ToString("N").Substring(0, 8);

        private string BuildTarget(string tag, string helperTag = null)
        {
            var dir = NewDir(tag);
            var helper = "FwHelper" + _id;
            var main = "FwTarget" + _id;
            Compile(helper, "namespace Fw { public static class Helper { public static string Tag => \"H" + (helperTag ?? tag) + "\"; } }", dir);
            Compile(main, "namespace Fw { public static class Probe { public static string Tag => \"M" + tag + "-\" + Helper.Tag; } }", dir,
                Path.Combine(dir, helper + ".dll"));
            return Path.Combine(dir, main + ".dll");
        }

        private static Generation Load(string path, string revitDir = null) =>
            Generation.Load(path, Array.Empty<Assembly>(), revitDir, _ => { });

        private static string Read(Generation generation, string property) =>
            (string)generation.Assembly.GetType("Fw.Probe").GetProperty(property).GetValue(null);

        [Fact]
        public void ARebuiltHelperIsUsedWhileTheOldGenerationIsStillLoaded()
        {
            var first = Load(BuildTarget("v1"));
            var second = Load(BuildTarget("v2"));

            Assert.Equal("Mv1-Hv1", Read(first, "Tag"));
            Assert.Equal("Mv2-Hv2", Read(second, "Tag"));
            Assert.NotEqual(first.Assembly, second.Assembly);
        }

        [Fact]
        public void LocationIsTheRealShadowPathAndFilesBesideTheDllAreReadable()
        {
            var dir = NewDir("loc");
            Directory.CreateDirectory(Path.Combine(dir, "data"));
            File.WriteAllText(Path.Combine(dir, "settings.txt"), "beside");
            File.WriteAllText(Path.Combine(dir, "data", "t.csv"), "x,y");
            var name = "FwLoc" + _id;
            Compile(name, "namespace Fw { public static class Probe { public static string Read(string n) => System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Probe).Assembly.Location), n)); } }", dir);

            var shadowed = new ShadowCopier(_ => { }, Path.Combine(_root, "shadow")).CopyToShadow(Path.Combine(dir, name + ".dll"));
            var generation = Load(shadowed);
            var read = generation.Assembly.GetType("Fw.Probe").GetMethod("Read");

            Assert.Equal(shadowed, generation.Assembly.Location);
            Assert.Equal("beside", (string)read.Invoke(null, new object[] { "settings.txt" }));
            Assert.Equal("x,y", (string)read.Invoke(null, new object[] { Path.Combine("data", "t.csv") }));
        }

        [Fact]
        public void ARevitNamedFileBesideTheTargetIsNotLoadedFromTheTarget()
        {
            var revitDir = NewDir("revit");
            var lib = "FwRevit" + _id;
            Compile(lib, "namespace Fw { public class ApiType { } }", revitDir);
            var dir = NewDir("rl");
            File.Copy(Path.Combine(revitDir, lib + ".dll"), Path.Combine(dir, lib + ".dll"));
            var libPath = Path.Combine(dir, lib + ".dll");
            Compile("FwUses" + _id, "namespace Fw { public static class Uses { public static System.Type T => typeof(ApiType); } }", dir, libPath);
            Compile("FwApiMain" + _id, "namespace Fw { public static class Probe { public static System.Type T => Uses.T; } }", dir, libPath, Path.Combine(dir, "FwUses" + _id + ".dll"));
            // The Revit process has this copy loaded under its own path before any generation asks for it.
            var revitCopy = Assembly.LoadFrom(Path.Combine(revitDir, lib + ".dll"));

            var generation = Load(Path.Combine(dir, "FwApiMain" + _id + ".dll"), revitDir);
            var type = (Type)generation.Assembly.GetType("Fw.Probe").GetProperty("T").GetValue(null);

            Assert.Same(revitCopy, type.Assembly);
            Assert.DoesNotContain(generation.Assemblies, a => a.GetName().Name.StartsWith(lib));
        }

        private string BuildFriends(string tag)
        {
            var dir = NewDir("ivt");
            var core = "FwCore" + _id;
            var friend = "FwFriend" + _id;
            Compile(core, "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"" + friend + "\")] namespace Fw { internal static class Secret { internal static string V => \"s\"; } }", dir);
            Compile(friend, "namespace Fw { public static class Friend { public static string V => Secret.V + \"" + tag + "\"; } }", dir, Path.Combine(dir, core + ".dll"));
            Compile("FwIvtMain" + _id, "namespace Fw { public static class Probe { public static string Tag => Friend.V; } }", dir, Path.Combine(dir, friend + ".dll"));
            return Path.Combine(dir, "FwIvtMain" + _id + ".dll");
        }

        [Fact]
        public void InternalsVisibleToSurvivesTheRename()
        {
            Load(BuildFriends("1"));
            // The friend changed, so it is renamed; the unchanged core names it and must be rewritten with it.
            var second = Load(BuildFriends("2"));

            Assert.Equal("s2", Read(second, "Tag"));
            Assert.Contains(second.Assemblies, a => a.GetName().Name.StartsWith("FwFriend" + _id + "_g"));
            Assert.Contains(second.Assemblies, a => a.GetName().Name.StartsWith("FwCore" + _id + "_g"));
        }

        private static string[] Names(Generation generation) => generation.Assemblies.Select(a => a.GetName().Name).ToArray();

        [Fact]
        public void TheFirstGenerationLoadsHelpersUnderTheirOwnNames()
        {
            var first = Load(BuildTarget("n1"));

            Assert.Contains("FwHelper" + _id, Names(first));
            Assert.DoesNotContain(Names(first), n => n.Contains("_g"));
        }

        [Fact]
        public void AnUnchangedHelperIsSharedWithTheNextGeneration()
        {
            var first = Load(BuildTarget("u1", "same"));
            var second = Load(BuildTarget("u2", "same"));

            Assert.Same(
                first.Assemblies.Single(a => a.GetName().Name == "FwHelper" + _id),
                second.Assemblies.Single(a => a.GetName().Name == "FwHelper" + _id));
            Assert.Equal("Mu1-Hsame", Read(first, "Tag"));
            Assert.Equal("Mu2-Hsame", Read(second, "Tag"));
        }

        [Fact]
        public void AChangedHelperIsRenamedForTheNextGeneration()
        {
            var first = Load(BuildTarget("c1", "h1"));
            var second = Load(BuildTarget("c2", "h2"));

            Assert.Contains(Names(second), n => n.StartsWith("FwHelper" + _id + "_g"));
            Assert.Equal("Mc1-Hh1", Read(first, "Tag"));
            Assert.Equal("Mc2-Hh2", Read(second, "Tag"));
        }

        [Fact]
        public void AHelperThatReferencesAChangedHelperIsRenamedToo()
        {
            string Build(string coreTag)
            {
                var dir = NewDir("chain");
                var core = "FwChainCore" + _id;
                var mid = "FwChainMid" + _id;
                Compile(core, "namespace Fw { public static class Core { public static string V => \"" + coreTag + "\"; } }", dir);
                Compile(mid, "namespace Fw { public static class Mid { public static string V => \"m-\" + Core.V; } }", dir, Path.Combine(dir, core + ".dll"));
                Compile("FwChainMain" + _id, "namespace Fw { public static class Probe { public static string Tag => Mid.V; } }", dir, Path.Combine(dir, mid + ".dll"));
                return Path.Combine(dir, "FwChainMain" + _id + ".dll");
            }

            Load(Build("c1"));
            var second = Load(Build("c2"));

            Assert.Equal("m-c2", Read(second, "Tag"));
            Assert.Contains(Names(second), n => n.StartsWith("FwChainMid" + _id + "_g"));
        }
    }
}
