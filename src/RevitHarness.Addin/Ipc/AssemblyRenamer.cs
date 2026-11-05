#nullable enable

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.Collections.Generic;
using System.IO;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// The .NET Framework binder satisfies a static reference to a simple-named assembly with any copy
    /// already loaded under that name, so a byte-loaded helper DLL of an older generation would shadow
    /// the rebuilt one forever. Renaming the helper (and the references to it) per generation gives
    /// every generation its own binding target. Only the assembly name changes; namespaces and types stay.
    /// </summary>
    internal static class AssemblyRenamer
    {
        public static (byte[] Dll, byte[]? Pdb) Rename(byte[] dll, byte[]? pdb, IReadOnlyDictionary<string, string> newNames, IEnumerable<string> searchDirectories)
        {
            // Writing an enum-typed constant (an optional parameter default) resolves the enum's defining assembly, which for Revit types lives in the Revit directory.
            using var resolver = new DefaultAssemblyResolver();
            foreach (var directory in searchDirectories) resolver.AddSearchDirectory(directory);
            var module = Read(dll, pdb, resolver, out var withSymbols);
            using (module)
            {
                if (newNames.TryGetValue(module.Assembly.Name.Name, out var self))
                    module.Assembly.Name.Name = self;
                foreach (var reference in module.AssemblyReferences)
                    if (newNames.TryGetValue(reference.Name, out var renamed))
                        reference.Name = renamed;
                RenameInternalsVisibleTo(module.Assembly, newNames);

                using var outDll = new MemoryStream();
                using var outPdb = new MemoryStream();
                module.Write(outDll, new WriterParameters
                {
                    WriteSymbols = withSymbols,
                    SymbolStream = withSymbols ? outPdb : null,
                    SymbolWriterProvider = withSymbols ? new PortablePdbWriterProvider() : null
                });
                return (outDll.ToArray(), withSymbols ? outPdb.ToArray() : null);
            }
        }

        // A renamed helper must stay a friend of its renamed siblings; the value is "Name" or "Name, PublicKey=...".
        private static void RenameInternalsVisibleTo(AssemblyDefinition assembly, IReadOnlyDictionary<string, string> newNames)
        {
            foreach (var attribute in assembly.CustomAttributes)
            {
                if (attribute.AttributeType.FullName != "System.Runtime.CompilerServices.InternalsVisibleToAttribute") continue;
                var argument = attribute.ConstructorArguments[0];
                if (!(argument.Value is string friend)) continue;
                var comma = friend.IndexOf(',');
                var name = (comma < 0 ? friend : friend.Substring(0, comma)).Trim();
                if (newNames.TryGetValue(name, out var renamed))
                    attribute.ConstructorArguments[0] = new CustomAttributeArgument(argument.Type, renamed + (comma < 0 ? "" : friend.Substring(comma)));
            }
        }

        private static ModuleDefinition Read(byte[] dll, byte[]? pdb, IAssemblyResolver resolver, out bool withSymbols)
        {
            if (pdb != null)
            {
                try
                {
                    var module = ModuleDefinition.ReadModule(new MemoryStream(dll), new ReaderParameters
                    {
                        ReadSymbols = true,
                        SymbolStream = new MemoryStream(pdb),
                        SymbolReaderProvider = new PortablePdbReaderProvider(),
                        InMemory = true,
                        AssemblyResolver = resolver
                    });
                    withSymbols = true;
                    return module;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is BadImageFormatException || ex is NotSupportedException)
                {
                    // A Windows PDB cannot be carried through the rewrite; the rewritten assembly loads without symbols.
                }
            }
            withSymbols = false;
            return ModuleDefinition.ReadModule(new MemoryStream(dll), new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        }
    }
}
