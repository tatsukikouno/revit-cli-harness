using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB.ExtensibleStorage;
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
        internal static void Write(string kind) => SampleLog.Write(kind, Tag, SampleTarget.Friend.FriendInfo.CoreTag);

        // What a real add-in does unmodified: find files beside its own DLL through Assembly.Location.
        internal static void WriteEnvironment()
        {
            var location = typeof(App).Assembly.Location;
            var directory = Path.GetDirectoryName(location) ?? "";
            string Beside(string relative)
            {
                var path = Path.Combine(directory, relative);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "missing";
            }
            SampleLog.Write("env", Tag, SampleTarget.Friend.FriendInfo.CoreTag,
                ("location", location),
                ("settings", Beside("sample.settings.txt")),
                ("table", Beside(Path.Combine("data", "table.csv"))),
                ("revitApi", SampleTarget.Friend.FriendInfo.RevitApiLocation),
                ("adWindows", SampleTarget.Friend.FriendInfo.AdWindowsLocation),
                ("vendorSchema", VendorSchema(directory)));
        }

        // A schema gated by a vendor id succeeds only when the id matches the running add-in's VendorId.
        // The id comes from a machine-local vendor.txt beside the DLL; without the file this is skipped.
        private static string VendorSchema(string directory)
        {
            var file = Path.Combine(directory, "vendor.txt");
            if (!File.Exists(file)) return "skipped";
            try
            {
                var id = File.ReadAllText(file).Trim();
                var guid = new Guid("0b9a4c0e-6a49-4d7c-9d3f-2f6f1f0a2c11");
                var schema = Schema.Lookup(guid);
                if (schema == null)
                {
                    var builder = new SchemaBuilder(guid);
                    builder.SetSchemaName("SampleTargetVendor");
                    builder.SetVendorId(id);
                    builder.AddSimpleField("Value", typeof(int));
                    schema = builder.Finish();
                }
                return "ok:" + schema.VendorId;
            }
            catch (Exception ex) { return "error:" + ex.GetType().Name + ":" + ex.Message; }
        }
    }

    public sealed class App : IExternalApplication
    {
        private EventHandler<DocumentChangedEventArgs>? _documentChanged;

        public Result OnStartup(UIControlledApplication application)
        {
            if (TargetLog.Features.Contains("panel"))
            {
                try { application.CreateRibbonTab("SampleTarget"); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                var panel = application.GetRibbonPanels("SampleTarget").FirstOrDefault(p => p.Name == "Sample")
                    ?? application.CreateRibbonPanel("SampleTarget", "Sample");
                if (TargetLog.Features.Contains("button"))
                    TargetRibbon.AddPushButton(panel, "Hello", $"Hello {TargetLog.Tag}", typeof(HelloCommand).FullName!);
                if (TargetLog.Features.Contains("many"))
                {
                    var many = application.GetRibbonPanels("SampleTarget").FirstOrDefault(p => p.Name == "Many")
                        ?? application.CreateRibbonPanel("SampleTarget", "Many");
                    var command = typeof(HelloCommand).FullName!;
                    for (var i = 0; i < 12; i++)
                        TargetRibbon.AddPushButton(many, $"Push{i:00}", $"Push {i:00} {TargetLog.Tag}", command);
                    var split = TargetRibbon.AddSplitButton(many, "Split1", "Split");
                    TargetRibbon.AddPushButton(split, "SplitA", $"SplitA {TargetLog.Tag}", command);
                    TargetRibbon.AddPushButton(split, "SplitB", $"SplitB {TargetLog.Tag}", command);
                    var pulldown = TargetRibbon.AddPulldownButton(many, "Pull1", "Pulldown");
                    TargetRibbon.AddPushButton(pulldown, "PullA", $"PullA {TargetLog.Tag}", command);
                    TargetRibbon.AddPushButton(pulldown, "PullB", $"PullB {TargetLog.Tag}", command);
                    TargetLog.Write("many " + string.Join(";", many.GetItems().Select(Describe)));
                }
            }
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
            TargetLog.WriteEnvironment();
            TargetLog.Write("startup");
            return Result.Succeeded;
        }

        internal static string Describe(RibbonItem item) => item switch
        {
            SplitButton s => $"{s.Name}:split[{string.Join(",", s.GetItems().Select(Describe))}]",
            PulldownButton d => $"{d.Name}:pulldown[{string.Join(",", d.GetItems().Select(Describe))}]",
            _ => item.Name
        };

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
