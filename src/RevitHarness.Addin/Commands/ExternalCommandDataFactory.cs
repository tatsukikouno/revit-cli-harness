#nullable enable

using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitHarness.Addin.Commands
{
    /// <summary>
    /// Builds the <see cref="ExternalCommandData"/> Revit would pass to an <see cref="IExternalCommand"/>.
    /// Only the constructor is non-public, so it is invoked by reflection and the rest is assigned normally.
    /// <c>--args</c> becomes <c>JournalData</c>, as a journal-driven command would read them.
    /// </summary>
    internal static class ExternalCommandDataFactory
    {
        public static ExternalCommandData Create(UIApplication uiapp, IDictionary<string, object> args)
        {
            var data = (ExternalCommandData)Activator.CreateInstance(typeof(ExternalCommandData), nonPublic: true)!;
            data.Application = uiapp;
            data.View = uiapp.ActiveUIDocument?.Document.ActiveView;
            data.JournalData = args.ToDictionary(pair => pair.Key, pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty);
            return data;
        }
    }
}
