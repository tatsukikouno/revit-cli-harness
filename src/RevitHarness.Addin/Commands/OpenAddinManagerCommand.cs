#nullable enable

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitHarness.Addin.UI;

namespace RevitHarness.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class OpenAddinManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var window = new AddinManagerWindow(uiapp);
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
