#nullable enable

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitHarness.Addin.UI;
using System.Diagnostics;
using System.Windows.Interop;

namespace RevitHarness.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class OpenAddinManagerCommand : IExternalCommand
    {
        private static AddinManagerWindow? _window;
        private static ExternalEvent? _externalEvent;
        private static AddinManagerEventHandler? _eventHandler;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_window != null)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            _eventHandler = new AddinManagerEventHandler(msg => { });
            _externalEvent = ExternalEvent.Create(_eventHandler);

            _window = new AddinManagerWindow(_externalEvent, _eventHandler);
            _window.Closed += (s, e) => { _window = null; };

            var helper = new WindowInteropHelper(_window);
            helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            _window.Show();

            return Result.Succeeded;
        }
    }
}
