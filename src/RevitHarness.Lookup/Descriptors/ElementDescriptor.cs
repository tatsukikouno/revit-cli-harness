#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class ElementDescriptor : Descriptor
    {
        public ElementDescriptor(Element element)
        {
            var category = element.Category?.Name ?? "N/A";
            var name = element.Name;
            var id = element.Id.Value;

            Name = string.IsNullOrEmpty(name) ? $"<{category}> [{id}]" : $"{name} <{category}> [{id}]";
            Description = $"Category: {category}, Id: {id}";
        }
    }
}
