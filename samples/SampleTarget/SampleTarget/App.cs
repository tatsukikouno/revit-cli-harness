using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using RevitHarness.Contracts;
using SampleTarget.Core;

namespace SampleTarget
{
    internal static class TargetLog
    {
        internal static readonly string Tag = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "BuildTag").Value ?? "";
        internal static readonly string Features = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "Features").Value ?? "";
        internal static void Write(string kind) => SampleLog.Write(kind, Tag, BuildInfo.Tag);
    }

    public sealed class App : IExternalApplication
    {
        private EventHandler<DocumentChangedEventArgs>? _documentChanged;

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab("SampleTarget"); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.GetRibbonPanels("SampleTarget").FirstOrDefault(p => p.Name == "Sample")
                ?? application.CreateRibbonPanel("SampleTarget", "Sample");
            TargetRibbon.AddPushButton(panel, "Hello", $"Hello {TargetLog.Tag}", typeof(HelloCommand).FullName!);
            if (TargetLog.Features.Contains("updater"))
            {
                var updater = new SampleUpdater(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(updater, true);
                UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(Level)),
                    Element.GetChangeTypeElementAddition());
            }
            if (TargetLog.Features.Contains("event"))
            {
                _documentChanged = (sender, args) => TargetLog.Write("event");
                application.ControlledApplication.DocumentChanged += _documentChanged;
            }
            TargetLog.Write("startup");
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try { if (TargetLog.Features.Contains("updater")) UpdaterRegistry.UnregisterUpdater(SampleUpdater.IdFor(application.ActiveAddInId)); }
            finally
            {
                if (_documentChanged != null)
                {
                    application.ControlledApplication.DocumentChanged -= _documentChanged;
                    _documentChanged = null;
                }
                TargetLog.Write("shutdown");
            }
            return Result.Succeeded;
        }
    }

    public sealed class SampleUpdater : IUpdater
    {
        private readonly UpdaterId _id;

        public SampleUpdater(AddInId owner) { _id = IdFor(owner); }

        public static UpdaterId IdFor(AddInId owner) => new UpdaterId(owner, new Guid("57b074d0-f416-48e8-8c77-4aefb091ee01"));

        public void Execute(UpdaterData data) => TargetLog.Write("updater");
        public string GetAdditionalInformation() => "Logs Level additions for hot reload verification.";
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;
        public UpdaterId GetUpdaterId() => _id;
        public string GetUpdaterName() => "SampleTarget Level logger";
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class HelloCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TargetLog.Write("ribbon");
            return Result.Succeeded;
        }
    }
}
