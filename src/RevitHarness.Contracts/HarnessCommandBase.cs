#nullable enable

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace RevitHarness.Contracts
{
    /// <summary>
    /// Abstract base class that implements both <see cref="IHarnessCommand"/> and <see cref="IExternalCommand"/>.
    /// Inherit from this class to create a command that can be executed both via the harness CLI
    /// and as a standard Revit ribbon button command.
    /// </summary>
    /// <remarks>
    /// If your class already inherits from another base class, implement <see cref="IHarnessCommand"/> directly instead.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public abstract class HarnessCommandBase : IHarnessCommand, IExternalCommand
    {
        /// <inheritdoc />
        public abstract string Execute(UIApplication uiapp, IDictionary<string, object> args);

        /// <summary>
        /// Bridges <see cref="IExternalCommand.Execute"/> to <see cref="IHarnessCommand.Execute"/>.
        /// </summary>
        Result IExternalCommand.Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Execute(commandData.Application, new Dictionary<string, object>());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
