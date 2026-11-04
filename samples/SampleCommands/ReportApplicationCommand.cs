using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SampleCommands
{
    /// <summary>
    /// An IExternalCommand that reads <c>commandData.Application</c>, as ribbon commands normally do.
    /// <code>revit-harness run-command --dll SampleCommands.dll --type SampleCommands.ReportApplicationCommand</code>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReportApplicationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            var view = commandData.View;
            message = $"Revit {uiapp.Application.VersionNumber}; active document: {doc?.Title ?? "(none)"}; active view: {view?.Name ?? "(none)"} (element id {view?.Id.ToString() ?? "-"})";
            return Result.Succeeded;
        }
    }
}
