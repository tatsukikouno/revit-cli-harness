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
                    var id = new UpdaterId(new AddInId(new Guid("4738EE1F-5DFA-4682-AB21-CDFC58CE299E")),
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
                        if (File.Exists(path)) File.Delete(path);
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
                case "open-doc":
                    if (uiapp.ActiveUIDocument != null) return JsonConvert.SerializeObject(new { title = uiapp.ActiveUIDocument.Document.Title });
                    var temp = Path.Combine(Path.GetTempPath(), "sample-target-probe.rvt");
                    if (File.Exists(temp)) File.Delete(temp);
                    var created = uiapp.Application.NewProjectDocument(UnitSystem.Metric);
                    created.SaveAs(temp);
                    created.Close(false);
                    return JsonConvert.SerializeObject(new { title = uiapp.OpenAndActivateDocument(temp).Document.Title });
                case "asm":
                    return JsonConvert.SerializeObject(AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => a.GetName().Name!.StartsWith("SampleTarget"))
                        .Select(a => new { name = a.GetName().Name, version = a.GetName().Version!.ToString(), location = a.IsDynamic ? "" : a.Location }));
                case "alc":
                    return JsonConvert.SerializeObject(new
                    {
                        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                        assemblies = AppDomain.CurrentDomain.GetAssemblies()
                            .Where(a => new[] { "RevitHarness.Addin", "RevitHarness.Contracts", "RevitAPI", "RevitAPIUI", "Newtonsoft.Json" }.Contains(a.GetName().Name))
                            .Select(a => new { name = a.GetName().Name, context = ContextOf(a) })
                    });
                default:
                    throw new ArgumentException("op must be state, clear-log, edit, click, ribbon, or alc.");
            }
        }

        private static string ContextOf(System.Reflection.Assembly assembly)
        {
#if NET48
            return "AppDomain";
#else
            var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly);
            return context == null ? "none" : $"{context.Name} collectible={context.IsCollectible}";
#endif
        }
    }
}
