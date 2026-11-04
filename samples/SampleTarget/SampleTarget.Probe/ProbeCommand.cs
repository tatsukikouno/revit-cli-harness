using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitHarness.Contracts;

namespace SampleTarget.Probe
{
    public sealed class ProbeCommand : IHarnessCommand
    {
        public string Execute(UIApplication uiapp, IDictionary<string, object> args)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitHarness", "sample-target", "log.jsonl");
            var op = args.TryGetValue("op", out var value) ? value?.ToString() : null;
            switch (op)
            {
                case "state":
                    var lines = File.Exists(path) ? File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(JObject.Parse).ToArray() : Array.Empty<JObject>();
                    var id = new UpdaterId(new AddInId(new Guid("4dabf61b-74b3-4a77-bab5-98f56ac2cc30")),
                        new Guid("57b074d0-f416-48e8-8c77-4aefb091ee01"));
                    return JsonConvert.SerializeObject(new { lines, updaterRegistered = UpdaterRegistry.IsUpdaterRegistered(id) });
                case "clear-log":
                    File.Delete(path);
                    return "{\"cleared\":true}";
                case "edit":
                    var active = uiapp.ActiveUIDocument?.Document;
                    var doc = active ?? uiapp.Application.NewProjectDocument(UnitSystem.Metric);
                    try
                    {
                        using (var transaction = new Transaction(doc, "SampleTarget probe"))
                        {
                            transaction.Start();
                            Level.Create(doc, 12.0);
                            transaction.Commit();
                        }
                        return JsonConvert.SerializeObject(new { title = doc.Title });
                    }
                    finally
                    {
                        if (active == null) doc.Close(false);
                    }
                case "click":
                    uiapp.PostCommand(RevitCommandId.LookupCommandId("CustomCtrl_%CustomCtrl_%SampleTarget%Sample%Hello"));
                    return "{\"posted\":true}";
                case "ribbon":
                    return JsonConvert.SerializeObject(new { panels = uiapp.GetRibbonPanels("SampleTarget")
                        .Select(p => new { name = p.Name, items = p.GetItems().Select(i => new { name = i.Name, text = i.ItemText }) }) });
                default:
                    throw new ArgumentException("op must be state, clear-log, edit, click, or ribbon.");
            }
        }
    }
}
