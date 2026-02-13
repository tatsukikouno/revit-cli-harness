#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class BoundingBoxXyzDescriptor : Descriptor
    {
        public BoundingBoxXyzDescriptor(BoundingBoxXYZ bbox)
        {
            var min = bbox.Min;
            var max = bbox.Max;
            Name = $"Min({min.X:F2}, {min.Y:F2}, {min.Z:F2}) Max({max.X:F2}, {max.Y:F2}, {max.Z:F2})";
        }
    }
}
