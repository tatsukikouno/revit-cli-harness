#nullable enable

using Autodesk.Revit.DB;
using LookupEngine.Abstractions.Decomposition;

namespace RevitHarness.Lookup.Descriptors
{
    public sealed class ParameterDescriptor : Descriptor
    {
        public ParameterDescriptor(Parameter parameter)
        {
            var name = parameter.Definition?.Name ?? "Unknown";
            var storageType = parameter.StorageType;
            string valueStr;

            switch (storageType)
            {
                case StorageType.String:
                    valueStr = parameter.AsString() ?? "(null)";
                    break;
                case StorageType.Integer:
                    valueStr = parameter.AsInteger().ToString();
                    break;
                case StorageType.Double:
                    valueStr = parameter.AsDouble().ToString("F4");
                    break;
                case StorageType.ElementId:
                    valueStr = parameter.AsElementId().Value.ToString();
                    break;
                default:
                    valueStr = parameter.AsValueString() ?? "(none)";
                    break;
            }

            Name = $"{name} = {valueStr}";
            Description = $"StorageType: {storageType}, IsShared: {parameter.IsShared}";
        }
    }
}
