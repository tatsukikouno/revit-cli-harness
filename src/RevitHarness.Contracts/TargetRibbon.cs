#nullable enable

using Autodesk.Revit.UI;
using System;

namespace RevitHarness.Contracts
{
    /// <summary>
    /// Implemented by the RevitHarness add-in. Binds a ribbon button to a command type of the
    /// currently loaded generation of a hot-reloaded target add-in.
    /// </summary>
    public interface ITargetRibbonHost
    {
        /// <summary>
        /// Adds a push button to <paramref name="panel"/> that runs <paramref name="commandType"/>
        /// (an <c>IExternalCommand</c> of the current generation). A button with the same
        /// <paramref name="name"/> on the same panel is reused: its text is updated and it is
        /// rebound to the new generation, so reloading never duplicates ribbon items.
        /// </summary>
        PushButton AddPushButton(RibbonPanel panel, string name, string text, string commandType);
    }

    /// <summary>
    /// Entry point for a target add-in's OnStartup when it runs under RevitHarness hot reload.
    /// </summary>
    public static class TargetRibbon
    {
        /// <summary>Set by the harness. Null when the add-in is not hosted by the harness.</summary>
        public static ITargetRibbonHost? Host { get; set; }

        /// <inheritdoc cref="ITargetRibbonHost.AddPushButton"/>
        public static PushButton AddPushButton(RibbonPanel panel, string name, string text, string commandType)
        {
            var host = Host ?? throw new InvalidOperationException(
                "No RevitHarness target host is active. Create the PushButtonData directly when the add-in is not hot reloaded.");
            return host.AddPushButton(panel, name, text, commandType);
        }
    }
}
