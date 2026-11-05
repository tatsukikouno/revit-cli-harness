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
    ///
    /// All members run on Revit's main thread.
    /// </summary>
    public sealed class TargetHost
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
            return status;
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
}
