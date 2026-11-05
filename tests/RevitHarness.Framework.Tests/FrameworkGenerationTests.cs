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
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(Path.Combine(outDir, name + ".dll"));
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

        private string BuildTarget(string tag)
        {
            var dir = NewDir(tag);
            var helper = "FwHelper" + _id;
            var main = "FwTarget" + _id;
            Compile(helper, "namespace Fw { public static class Helper { public static string Tag => \"H" + tag + "\"; } }", dir);
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

        [Fact]
        public void InternalsVisibleToSurvivesTheRename()
        {
            var dir = NewDir("ivt");
            var core = "FwCore" + _id;
            var friend = "FwFriend" + _id;
            Compile(core, "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"" + friend + "\")] namespace Fw { internal static class Secret { internal static string V => \"s\"; } }", dir);
            Compile(friend, "namespace Fw { public static class Friend { public static string V => Secret.V; } }", dir, Path.Combine(dir, core + ".dll"));
            Compile("FwIvtMain" + _id, "namespace Fw { public static class Probe { public static string Tag => Friend.V; } }", dir, Path.Combine(dir, friend + ".dll"));

            var generation = Load(Path.Combine(dir, "FwIvtMain" + _id + ".dll"));

            Assert.Equal("s", Read(generation, "Tag"));
        }
    }
}
