#nullable enable

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitHarness.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RevitHarness.Addin.Ipc
{
    public enum TargetState
    {
        Unloaded,
        Started
    }

    /// <summary>
    /// Owns the one hot-reloaded target add-in: its lifecycle (OnShutdown, dispose, load, OnStartup)
    /// and the ribbon slots that stay bound to Revit while generations come and go.
    /// All members run on Revit's main thread.
    /// </summary>
    public sealed class TargetHost : ITargetRibbonHost
    {
        public static TargetHost? Instance { get; set; }

        private sealed class Started
        {
            public Started(int number, Generation generation, IExternalApplication app, string sourcePath, string typeName)
            {
                Number = number;
                Generation = generation;
                App = app;
                SourcePath = sourcePath;
                TypeName = typeName;
                LoadedAt = DateTime.Now;
            }

            public int Number { get; }
            public Generation Generation { get; }
            public IExternalApplication App { get; }
            public string SourcePath { get; }
            public string TypeName { get; }
            public DateTime LoadedAt { get; }
        }

        private sealed class SavedTarget
        {
            [JsonProperty("assemblyPath")] public string AssemblyPath { get; set; } = string.Empty;
            [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        }

        private readonly ShadowCopier _shadow;
        private readonly Action<string> _log;
        private readonly string _stateFile;
        private readonly SlotTable _slots = new SlotTable();
        private readonly List<WeakReference> _old = new List<WeakReference>();
        private UIControlledApplication? _uic;
        private Started? _current;
        private int _generationsCreated;

        public TargetHost(ShadowCopier shadow, Action<string> log)
        {
            _shadow = shadow;
            _log = log;
            _stateFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitHarness",
                "target.json");
        }

        public static Assembly[] HostAssemblies { get; } =
        {
            typeof(ITestEntryPoint).Assembly,
            typeof(IExternalApplication).Assembly,
            typeof(ElementSet).Assembly
        };

        // RevitAPI.dll lives in the Revit install directory; every assembly Revit ships sits beside it.
        public static string? RevitDirectory { get; } = Path.GetDirectoryName(typeof(ElementSet).Assembly.Location);

        public void Attach(UIControlledApplication application) => _uic = application;

        public TargetState State => _current == null ? TargetState.Unloaded : TargetState.Started;

        public IReadOnlyCollection<Assembly> ResidentAssemblies =>
            _current?.Generation.Assemblies ?? (IReadOnlyCollection<Assembly>)Array.Empty<Assembly>();

        public Assembly? TryGetResidentAssembly(string dllPath)
        {
            var current = _current;
            if (current == null) return null;
            return string.Equals(Path.GetFullPath(dllPath), current.SourcePath, StringComparison.OrdinalIgnoreCase)
                ? current.Generation.Assembly
                : null;
        }

        public void RestoreSaved()
        {
            try
            {
                if (!File.Exists(_stateFile)) return;
                var saved = JsonConvert.DeserializeObject<SavedTarget>(File.ReadAllText(_stateFile));
                if (saved == null || !File.Exists(saved.AssemblyPath)) return;
                Reload(saved.AssemblyPath, saved.Type);
            }
            catch (Exception ex)
            {
                _log($"Restoring the saved target failed (non-fatal): {ex.Message}");
            }
        }

        public ReloadResult Reload(string dllPath, string typeName)
        {
            var uic = _uic ?? throw new InvalidOperationException("The harness has no UIControlledApplication yet.");
            var fullPath = Path.GetFullPath(dllPath);
            var result = new ReloadResult();

            if (_current != null)
            {
                result.PreviousGeneration = _current.Number;
                var lifetime = Stop(uic, result.Warnings);
                result.PreviousReleased = Released(lifetime);
            }

            Generation? generation = null;
            IExternalApplication? app = null;
            int number;
            try
            {
                var shadowPath = _shadow.CopyToShadow(fullPath);
                generation = Generation.Load(shadowPath, HostAssemblies, RevitDirectory, _log);
                number = ++_generationsCreated;
                app = Create(generation, typeName);
                var started = app.OnStartup(uic);
                if (started != Result.Succeeded)
                    throw new InvalidOperationException($"{typeName}.OnStartup returned {started}");
            }
            catch
            {
                if (app != null)
                {
                    try { app.OnShutdown(uic); }
                    catch (Exception ex) { _log($"OnShutdown after a failed OnStartup threw: {ex.Message}"); }
                }
                app = null;
                if (File.Exists(_stateFile)) File.Delete(_stateFile);
                if (generation != null)
                {
                    _old.Add(generation.Lifetime);
                    generation.Dispose();
                }
                throw;
            }

            _current = new Started(number, generation, app, fullPath, typeName);
            result.Generation = number;
            File.WriteAllText(_stateFile, JsonConvert.SerializeObject(new SavedTarget { AssemblyPath = fullPath, Type = typeName }));
            _log($"Target generation {number} started: {typeName}");
            _shadow.Cleanup();
            return result;
        }

        public UnloadResult Unload()
        {
            var result = new UnloadResult();
            if (File.Exists(_stateFile)) File.Delete(_stateFile);
            if (_current == null) return result;

            result.Generation = _current.Number;
            var lifetime = Stop(_uic!, result.Warnings);
            result.Released = Released(lifetime);
            result.Unloaded = true;
            _shadow.Cleanup();
            return result;
        }

        public void StopForRevitExit()
        {
            if (_current != null) Stop(_uic!, new List<string>());
        }

        public TargetStatusResult Status()
        {
            var current = _current;
            _old.RemoveAll(w => !w.IsAlive);
            var status = new TargetStatusResult
            {
                State = State.ToString(),
                Generation = current?.Number ?? 0,
                AssemblyPath = current?.SourcePath,
                Type = current?.TypeName,
                LoadedAt = current?.LoadedAt.ToString("O"),
                GenerationsCreated = _generationsCreated,
                Runtime = RuntimeInformation.FrameworkDescription,
                WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64
            };
#if !NET48
            status.LiveOldGenerations = _old.Count;
#endif
            foreach (var occupied in _slots.Occupied())
                status.RibbonSlots.Add(new RibbonSlotInfo { Slot = occupied.Key, Panel = occupied.Value.Panel, Name = occupied.Value.Name, CommandType = occupied.Value.CommandType });
            return status;
        }

        public Result ExecuteSlot(int index, ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var current = _current;
            var slot = _slots.Get(index);
            if (current == null || slot == null)
            {
                message = "No target add-in generation is loaded. Run `revit-harness reload` first.";
                return Result.Failed;
            }

            var type = current.Generation.Assembly.GetType(slot.CommandType);
            if (type == null || !typeof(IExternalCommand).IsAssignableFrom(type))
            {
                message = $"{slot.CommandType} is not an IExternalCommand of generation {current.Number}.";
                return Result.Failed;
            }
            var command = (IExternalCommand)Activator.CreateInstance(type)!;
            return command.Execute(commandData, ref message, elements);
        }

        public PushButton AddPushButton(RibbonPanel panel, string name, string text, string commandType) =>
            BindPushButton(panel.GetItems(), panel.Name, name, text, commandType, panel.AddItem);

        public PushButton AddPushButton(SplitButton split, string name, string text, string commandType) =>
            BindPushButton(split.GetItems(), split.Name, name, text, commandType, split.AddPushButton);

        public PushButton AddPushButton(PulldownButton pulldown, string name, string text, string commandType) =>
            BindPushButton(pulldown.GetItems(), pulldown.Name, name, text, commandType, pulldown.AddPushButton);

        public PulldownButton AddPulldownButton(RibbonPanel panel, string name, string text) =>
            ReuseOrAdd<PulldownButton>(panel.GetItems(), panel.Name, name, text, () => new PulldownButtonData(name, text), panel.AddItem);

        public SplitButton AddSplitButton(RibbonPanel panel, string name, string text) =>
            ReuseOrAdd<SplitButton>(panel.GetItems(), panel.Name, name, text, () => new SplitButtonData(name, text), panel.AddItem);

        private static T ReuseOrAdd<T>(IEnumerable<RibbonItem> siblings, string owner, string name, string text, Func<RibbonItemData> data, Func<RibbonItemData, RibbonItem> add)
            where T : RibbonItem
        {
            var existing = siblings.FirstOrDefault(i => i.Name == name);
            if (existing == null) return (T)add(data());
            if (!(existing is T typed))
                throw new InvalidOperationException($"Ribbon item '{name}' on '{owner}' is a {existing.GetType().Name}, not a {typeof(T).Name}.");
            typed.ItemText = text;
            return typed;
        }

        private PushButton BindPushButton(IEnumerable<RibbonItem> siblings, string owner, string name, string text, string commandType, Func<PushButtonData, RibbonItem> add)
        {
            var slotClass = typeof(TargetSlot0).Namespace + ".TargetSlot";
            // The button already inside this very owner identifies its slot, so same-named panels on different tabs never share one.
            var existing = siblings.OfType<PushButton>().FirstOrDefault(b => b.Name == name);
            if (existing != null)
            {
                if (!existing.ClassName.StartsWith(slotClass, StringComparison.Ordinal)
                    || !int.TryParse(existing.ClassName.Substring(slotClass.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var existingIndex)
                    || _slots.Get(existingIndex) == null)
                    throw new InvalidOperationException($"Ribbon button '{name}' on '{owner}' was not created by the harness.");
                _slots.Get(existingIndex)!.CommandType = commandType;
                existing.ItemText = text;
                return existing;
            }

            var index = _slots.Allocate(owner, name, commandType);
            var data = new PushButtonData(name, text, typeof(TargetHost).Assembly.Location, slotClass + index);
            return (PushButton)add(data);
        }

        private static IExternalApplication Create(Generation generation, string typeName)
        {
            var type = generation.Assembly.GetType(typeName)
                ?? throw new InvalidOperationException($"Type not found: {typeName}");
            return Activator.CreateInstance(type) as IExternalApplication
                ?? throw new InvalidOperationException($"{typeName} does not implement IExternalApplication");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference Stop(UIControlledApplication uic, List<string> warnings)
        {
            var current = _current!;
            _current = null;
            try
            {
                if (current.App.OnShutdown(uic) == Result.Failed)
                    warnings.Add($"{current.TypeName}.OnShutdown returned Failed");
            }
            catch (Exception ex)
            {
                warnings.Add($"{current.TypeName}.OnShutdown threw: {ex.Message}");
                _log($"Target OnShutdown threw: {ex}");
            }

            var lifetime = current.Generation.Lifetime;
            current.Generation.Dispose();
            _old.Add(lifetime);
            return lifetime;
        }

        private static bool? Released(WeakReference lifetime)
        {
#if NET48
            return null;
#else
            return Generation.WaitUntilCollected(lifetime);
#endif
        }
    }

    /// <summary>
    /// The assembly a run-tests/run-command/scan call executes: the resident target generation when the
    /// path is the target's own DLL, otherwise a throwaway generation that is disposed with the lease.
    /// </summary>
    public sealed class AssemblyLease : IDisposable
    {
        private readonly Generation? _generation;

        private AssemblyLease(Assembly assembly, Generation? generation)
        {
            Assembly = assembly;
            _generation = generation;
        }

        public Assembly Assembly { get; }

        public static AssemblyLease Open(ShadowCopier shadow, string dllPath, Action<string> log)
        {
            var resident = TargetHost.Instance?.TryGetResidentAssembly(dllPath);
            if (resident != null) return new AssemblyLease(resident, null);
            var hosts = TargetHost.HostAssemblies.Concat(TargetHost.Instance?.ResidentAssemblies ?? Array.Empty<Assembly>());
            var generation = Generation.Load(shadow.CopyToShadow(dllPath), hosts, TargetHost.RevitDirectory, log);
            return new AssemblyLease(generation.Assembly, generation);
        }

        public void Dispose() => _generation?.Dispose();
    }

    public abstract class TargetSlotCommand : IExternalCommand
    {
        private readonly int _slot;

        protected TargetSlotCommand(int slot) { _slot = slot; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var host = TargetHost.Instance;
            if (host == null)
            {
                message = "RevitHarness is not running.";
                return Result.Failed;
            }
            return host.ExecuteSlot(_slot, commandData, ref message, elements);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot0 : TargetSlotCommand { public TargetSlot0() : base(0) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot1 : TargetSlotCommand { public TargetSlot1() : base(1) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot2 : TargetSlotCommand { public TargetSlot2() : base(2) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot3 : TargetSlotCommand { public TargetSlot3() : base(3) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot4 : TargetSlotCommand { public TargetSlot4() : base(4) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot5 : TargetSlotCommand { public TargetSlot5() : base(5) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot6 : TargetSlotCommand { public TargetSlot6() : base(6) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot7 : TargetSlotCommand { public TargetSlot7() : base(7) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot8 : TargetSlotCommand { public TargetSlot8() : base(8) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot9 : TargetSlotCommand { public TargetSlot9() : base(9) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot10 : TargetSlotCommand { public TargetSlot10() : base(10) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot11 : TargetSlotCommand { public TargetSlot11() : base(11) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot12 : TargetSlotCommand { public TargetSlot12() : base(12) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot13 : TargetSlotCommand { public TargetSlot13() : base(13) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot14 : TargetSlotCommand { public TargetSlot14() : base(14) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot15 : TargetSlotCommand { public TargetSlot15() : base(15) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot16 : TargetSlotCommand { public TargetSlot16() : base(16) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot17 : TargetSlotCommand { public TargetSlot17() : base(17) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot18 : TargetSlotCommand { public TargetSlot18() : base(18) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot19 : TargetSlotCommand { public TargetSlot19() : base(19) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot20 : TargetSlotCommand { public TargetSlot20() : base(20) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot21 : TargetSlotCommand { public TargetSlot21() : base(21) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot22 : TargetSlotCommand { public TargetSlot22() : base(22) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot23 : TargetSlotCommand { public TargetSlot23() : base(23) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot24 : TargetSlotCommand { public TargetSlot24() : base(24) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot25 : TargetSlotCommand { public TargetSlot25() : base(25) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot26 : TargetSlotCommand { public TargetSlot26() : base(26) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot27 : TargetSlotCommand { public TargetSlot27() : base(27) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot28 : TargetSlotCommand { public TargetSlot28() : base(28) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot29 : TargetSlotCommand { public TargetSlot29() : base(29) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot30 : TargetSlotCommand { public TargetSlot30() : base(30) { } }
    [Transaction(TransactionMode.Manual)]
    public sealed class TargetSlot31 : TargetSlotCommand { public TargetSlot31() : base(31) { } }
}
