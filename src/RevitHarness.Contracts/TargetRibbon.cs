#nullable enable

using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RevitHarness.Contracts
{
    /// <summary>
    /// Implemented by the RevitHarness add-in. Binds ribbon buttons to command types of the
    /// currently loaded generation of a hot-reloaded target add-in. An item with the same name
    /// in the same panel (or container) is reused and rebound, so reloading never duplicates ribbon items.
    /// </summary>
    public interface ITargetRibbonHost
    {
        /// <summary>
        /// Adds a push button to <paramref name="panel"/> that runs <paramref name="commandType"/>
        /// (an <c>IExternalCommand</c> of the current generation), or reuses the existing one,
        /// updating its text and rebinding it to the new generation.
        /// </summary>
        PushButton AddPushButton(RibbonPanel panel, string name, string text, string commandType);

        /// <summary>Adds (or reuses) a push button inside a split button. Same reuse rule as the panel overload.</summary>
        PushButton AddPushButton(SplitButton split, string name, string text, string commandType);

        /// <summary>Adds (or reuses) a push button inside a pulldown button. Same reuse rule as the panel overload.</summary>
        PushButton AddPushButton(PulldownButton pulldown, string name, string text, string commandType);

        /// <summary>Adds a split button to <paramref name="panel"/>, or reuses the one with that name.</summary>
        SplitButton AddSplitButton(RibbonPanel panel, string name, string text);

        /// <summary>Adds a pulldown button to <paramref name="panel"/>, or reuses the one with that name.</summary>
        PulldownButton AddPulldownButton(RibbonPanel panel, string name, string text);
    }

    /// <summary>
    /// Entry point for a target add-in's OnStartup. Under RevitHarness hot reload the calls bind to
    /// permanent harness command stubs; outside the harness (<see cref="Host"/> is null) they create plain
    /// Revit ribbon items that run <c>commandType</c> from the calling assembly.
    /// </summary>
    public static class TargetRibbon
    {
        /// <summary>Set by the harness. Null when the add-in is not hosted by the harness.</summary>
        public static ITargetRibbonHost? Host { get; set; }

        /// <inheritdoc cref="ITargetRibbonHost.AddPushButton(RibbonPanel, string, string, string)"/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static PushButton AddPushButton(RibbonPanel panel, string name, string text, string commandType)
        {
            var host = Host;
            if (host != null) return host.AddPushButton(panel, name, text, commandType);
            return NativePush(panel.GetItems(), panel.AddItem, name, text, commandType, Assembly.GetCallingAssembly());
        }

        /// <inheritdoc cref="ITargetRibbonHost.AddPushButton(SplitButton, string, string, string)"/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static PushButton AddPushButton(SplitButton split, string name, string text, string commandType)
        {
            var host = Host;
            if (host != null) return host.AddPushButton(split, name, text, commandType);
            return NativePush(split.GetItems(), split.AddPushButton, name, text, commandType, Assembly.GetCallingAssembly());
        }

        /// <inheritdoc cref="ITargetRibbonHost.AddPushButton(PulldownButton, string, string, string)"/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static PushButton AddPushButton(PulldownButton pulldown, string name, string text, string commandType)
        {
            var host = Host;
            if (host != null) return host.AddPushButton(pulldown, name, text, commandType);
            return NativePush(pulldown.GetItems(), pulldown.AddPushButton, name, text, commandType, Assembly.GetCallingAssembly());
        }

        /// <inheritdoc cref="ITargetRibbonHost.AddSplitButton"/>
        public static SplitButton AddSplitButton(RibbonPanel panel, string name, string text)
        {
            var host = Host;
            if (host != null) return host.AddSplitButton(panel, name, text);
            var existing = panel.GetItems().OfType<SplitButton>().FirstOrDefault(i => i.Name == name);
            return existing ?? (SplitButton)panel.AddItem(new SplitButtonData(name, text));
        }

        /// <inheritdoc cref="ITargetRibbonHost.AddPulldownButton"/>
        public static PulldownButton AddPulldownButton(RibbonPanel panel, string name, string text)
        {
            var host = Host;
            if (host != null) return host.AddPulldownButton(panel, name, text);
            var existing = panel.GetItems().OfType<PulldownButton>().FirstOrDefault(i => i.Name == name);
            return existing ?? (PulldownButton)panel.AddItem(new PulldownButtonData(name, text));
        }

        private static PushButton NativePush(
            System.Collections.Generic.IEnumerable<RibbonItem> siblings,
            Func<PushButtonData, RibbonItem> add,
            string name, string text, string commandType, Assembly caller)
        {
            var existing = siblings.OfType<PushButton>().FirstOrDefault(b => b.Name == name);
            if (existing != null) return existing;
            return (PushButton)add(new PushButtonData(name, text, caller.Location, commandType));
        }
    }
}
