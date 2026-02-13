#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;
using System;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class CurveDescriptor : Descriptor
    {
        public CurveDescriptor(Curve curve)
        {
            try
            {
                var length = curve.Length;
                Name = $"{curve.GetType().Name} (Length: {length:F4})";
            }
            catch (Exception)
            {
                Name = curve.GetType().Name;
            }
        }
    }
}
