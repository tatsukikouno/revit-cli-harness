#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class DocumentDescriptor : Descriptor
    {
        public DocumentDescriptor(Document document)
        {
            Name = document.Title;
            Description = $"Path: {document.PathName}";
        }
    }
}
