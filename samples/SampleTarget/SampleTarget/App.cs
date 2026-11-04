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
            var updater = new SampleUpdater();
            UpdaterRegistry.RegisterUpdater(updater, true);
            UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(Level)),
                Element.GetChangeTypeElementAddition());
            _documentChanged = (sender, args) => TargetLog.Write("event");
            application.ControlledApplication.DocumentChanged += _documentChanged;
            TargetLog.Write("startup");
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try { UpdaterRegistry.UnregisterUpdater(SampleUpdater.Id); }
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
        public static readonly UpdaterId Id = new UpdaterId(
            new AddInId(new Guid("4dabf61b-74b3-4a77-bab5-98f56ac2cc30")),
            new Guid("57b074d0-f416-48e8-8c77-4aefb091ee01"));
        public void Execute(UpdaterData data) => TargetLog.Write("updater");
        public string GetAdditionalInformation() => "Logs Level additions for hot reload verification.";
        public ChangePriority GetChangePriority() => ChangePriority.LevelsGrids;
        public UpdaterId GetUpdaterId() => Id;
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
