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
        public const int SlotCount = 8;

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

        private sealed class Slot
        {
            public string Panel = string.Empty;
            public string Name = string.Empty;
            public string CommandType = string.Empty;
        }

        private sealed class SavedTarget
        {
            [JsonProperty("assemblyPath")] public string AssemblyPath { get; set; } = string.Empty;
            [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        }

        private readonly ShadowCopier _shadow;
        private readonly Action<string> _log;
        private readonly string _stateFile;
        private readonly Slot?[] _slots = new Slot?[SlotCount];
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

            var shadowPath = _shadow.CopyToShadow(fullPath);
            var generation = Generation.Load(shadowPath, HostAssemblies, _log);
            var number = ++_generationsCreated;
            IExternalApplication? app = null;
            try
            {
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
                _old.Add(generation.Lifetime);
                generation.Dispose();
                throw;
            }

            _current = new Started(number, generation, app, fullPath, typeName);
            result.Generation = number;
            File.WriteAllText(_stateFile, JsonConvert.SerializeObject(new SavedTarget { AssemblyPath = fullPath, Type = typeName }));
            _log($"Target generation {number} started: {typeName}");
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
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot != null)
                    status.RibbonSlots.Add(new RibbonSlotInfo { Slot = i, Panel = slot.Panel, Name = slot.Name, CommandType = slot.CommandType });
            }
            return status;
        }

        public Result ExecuteSlot(int index, ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var current = _current;
            var slot = _slots[index];
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

        public PushButton AddPushButton(RibbonPanel panel, string name, string text, string commandType)
        {
            var index = Array.FindIndex(_slots, s => s != null && s.Panel == panel.Name && s.Name == name);
            if (index >= 0)
            {
                _slots[index]!.CommandType = commandType;
                var existing = panel.GetItems().OfType<PushButton>().FirstOrDefault(b => b.Name == name);
                if (existing != null)
                {
                    existing.ItemText = text;
                    return existing;
                }
            }
            else
            {
                index = Array.FindIndex(_slots, s => s == null);
                if (index < 0)
                    throw new InvalidOperationException($"At most {SlotCount} hot reloaded ribbon buttons are supported.");
            }

            var data = new PushButtonData(name, text, typeof(TargetHost).Assembly.Location, typeof(TargetSlot0).Namespace + ".TargetSlot" + index);
            var button = (PushButton)panel.AddItem(data);
            _slots[index] = new Slot { Panel = panel.Name, Name = name, CommandType = commandType };
            return button;
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
            var generation = Generation.Load(shadow.CopyToShadow(dllPath), hosts, log);
            return new AssemblyLease(generation.Assembly, generation);
        }

        public void Dispose() => _generation?.Dispose();
    }

    [Transaction(TransactionMode.Manual)]
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

    public sealed class TargetSlot0 : TargetSlotCommand { public TargetSlot0() : base(0) { } }
    public sealed class TargetSlot1 : TargetSlotCommand { public TargetSlot1() : base(1) { } }
    public sealed class TargetSlot2 : TargetSlotCommand { public TargetSlot2() : base(2) { } }
    public sealed class TargetSlot3 : TargetSlotCommand { public TargetSlot3() : base(3) { } }
    public sealed class TargetSlot4 : TargetSlotCommand { public TargetSlot4() : base(4) { } }
    public sealed class TargetSlot5 : TargetSlotCommand { public TargetSlot5() : base(5) { } }
    public sealed class TargetSlot6 : TargetSlotCommand { public TargetSlot6() : base(6) { } }
    public sealed class TargetSlot7 : TargetSlotCommand { public TargetSlot7() : base(7) { } }
}
