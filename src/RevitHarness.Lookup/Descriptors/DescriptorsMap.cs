#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;
using LookupEngine.Descriptors;
using System;

namespace RevitHarness.Lookup.Descriptors
{
    /// <summary>
    /// Maps Revit types to custom descriptors for richer inspection output.
    /// </summary>
    public static class DescriptorsMap
    {
        public static Descriptor FindDescriptor(object? obj, Type? type)
        {
            return obj switch
            {
                // Tier 1
                Element element => new ElementDescriptor(element),
                ElementId elementId => new ElementIdDescriptor(elementId),
                Parameter parameter => new ParameterDescriptor(parameter),
                Document document => new DocumentDescriptor(document),

                // Tier 2
                XYZ xyz => new XyzDescriptor(xyz),
                BoundingBoxXYZ bbox => new BoundingBoxXyzDescriptor(bbox),

                // Tier 3
                Curve curve => new CurveDescriptor(curve),
                Color color => new ColorDescriptor(color),

                // Fallback
                _ => new ObjectDescriptor(obj)
            };
        }
    }
}
