#nullable enable

using Autodesk.Revit.UI;
using System;
using System.Reflection;

namespace RevitHarness.Addin.Commands
{
    /// <summary>
    /// Builds the <see cref="ExternalCommandData"/> Revit would pass to an <see cref="IExternalCommand"/>.
    /// The type has no public constructor and no public setters, so it is created and filled by reflection.
    /// </summary>
    internal static class ExternalCommandDataFactory
    {
        public static ExternalCommandData Create(UIApplication uiapp)
        {
            var data = (ExternalCommandData)Activator.CreateInstance(typeof(ExternalCommandData), nonPublic: true)!;
            Set(data, nameof(ExternalCommandData.Application), uiapp);
            Set(data, nameof(ExternalCommandData.View), uiapp.ActiveUIDocument?.Document.ActiveView);
            return data;
        }

        private static void Set(ExternalCommandData data, string property, object? value)
        {
            var setter = typeof(ExternalCommandData).GetProperty(property)?.GetSetMethod(nonPublic: true)
                ?? throw new MissingMethodException(nameof(ExternalCommandData), "set_" + property);
            setter.Invoke(data, new[] { value });
        }
    }
}
