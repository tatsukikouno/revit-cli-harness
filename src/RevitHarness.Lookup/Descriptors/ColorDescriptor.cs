#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class ColorDescriptor : Descriptor
    {
        public ColorDescriptor(Color color)
        {
            if (color.IsValid)
            {
                Name = $"RGB({color.Red}, {color.Green}, {color.Blue})";
            }
            else
            {
                Name = "Invalid";
            }
        }
    }
}
