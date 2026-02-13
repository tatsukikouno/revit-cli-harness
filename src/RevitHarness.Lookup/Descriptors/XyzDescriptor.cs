#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class XyzDescriptor : Descriptor
    {
        public XyzDescriptor(XYZ xyz)
        {
            Name = $"({xyz.X:F4}, {xyz.Y:F4}, {xyz.Z:F4})";
        }
    }
}
