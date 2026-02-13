#nullable enable

using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace RevitHarness.Contracts
{
    /// <summary>
    /// Simplified command interface for harness execution.
    /// Use this instead of IExternalCommand for commands that need to be executed via the harness.
    /// </summary>
    public interface IHarnessCommand
    {
        /// <summary>
        /// Executes the command.
        /// </summary>
        /// <param name="uiapp">The UIApplication instance.</param>
        /// <param name="args">Arguments passed from the IPC request.</param>
        /// <returns>JSON string with the result.</returns>
        string Execute(UIApplication uiapp, IDictionary<string, object> args);
    }
}
