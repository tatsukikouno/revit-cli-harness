#nullable enable

using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace RevitHarness.Contracts
{
    /// <summary>
    /// Interface for test entry points that can be executed via the harness.
    /// Test DLLs should implement this interface to be callable via runTests operation.
    /// </summary>
    public interface ITestEntryPoint
    {
        /// <summary>
        /// Executes tests and returns results as JSON string.
        /// </summary>
        /// <param name="uiapp">The UIApplication instance for Revit API access.</param>
        /// <param name="args">Arguments passed from the IPC request (e.g., filter, headless).</param>
        /// <returns>JSON string with test results. Expected to contain "passed" boolean field.</returns>
        string Run(UIApplication uiapp, IDictionary<string, object> args);
    }
}
