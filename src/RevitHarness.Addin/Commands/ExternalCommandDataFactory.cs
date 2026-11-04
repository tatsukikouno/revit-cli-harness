#nullable enable

using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace RevitHarness.Addin.Commands
{
    /// <summary>
    /// Builds the <see cref="ExternalCommandData"/> Revit would pass to an <see cref="IExternalCommand"/>.
    /// Only the constructor is non-public, so it is invoked by reflection and the rest is assigned normally.
    /// </summary>
    internal static class ExternalCommandDataFactory
    {
        public static ExternalCommandData Create(UIApplication uiapp)
        {
            var data = (ExternalCommandData)Activator.CreateInstance(typeof(ExternalCommandData), nonPublic: true)!;
            data.Application = uiapp;
            data.View = uiapp.ActiveUIDocument?.Document.ActiveView;
            data.JournalData = new Dictionary<string, string>();
            return data;
        }
    }
}
