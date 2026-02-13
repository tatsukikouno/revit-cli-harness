#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class ElementIdDescriptor : Descriptor
    {
        public ElementIdDescriptor(ElementId elementId)
        {
            Name = elementId.Value.ToString();
            Description = elementId.Value < 0 ? "BuiltIn" : null;
        }
    }
}
