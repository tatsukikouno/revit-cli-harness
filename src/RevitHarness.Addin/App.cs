#nullable enable

using Autodesk.Revit.UI;
using RevitHarness.Addin.Commands;
using RevitHarness.Addin.DialogHandling;
using RevitHarness.Addin.Ipc;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitHarness.Addin
{
    public class App : IExternalApplication
    {
        private IpcServer? _server;
        private ExternalEvent? _externalEvent;
        private HarnessEventHandler? _eventHandler;
        private Timer? _processingTimer;
        private DialogHooks? _dialogHooks;
        private static readonly object _logLock = new object();
        private static StreamWriter? _logWriter;

        /// <summary>
        /// Called when Revit starts up.
        /// </summary>
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                BundledDependencyResolver.Install();
                InitializeLogging();
                Log("RevitHarness add-in starting...");

                // Create IPC server
                _server = new IpcServer(Log);
                _server.DirectHandler = DialogIpcHandler.TryHandle;

                // Create event handler
                _eventHandler = new HarnessEventHandler(_server, Log);

                // Create external event
                _externalEvent = ExternalEvent.Create(_eventHandler);

                // Dialog auto-handling (fixed in this add-in, never in hot-loaded user DLLs)
                try
                {
                    _dialogHooks = new DialogHooks(Log);
                    _dialogHooks.Subscribe(application);
                }
                catch (Exception dialogEx)
                {
                    Log($"Dialog hooks failed (non-fatal): {dialogEx.Message}");
                }

                // Start IPC server
                _server.Start();

                // Set up periodic processing timer to raise external event
                _processingTimer = new Timer(
                    ProcessPendingRequests,
                    null,
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(100));

                // Create ribbon UI (non-fatal if it fails)
                try
                {
                    CreateRibbon(application);
                }
                catch (Exception ribbonEx)
                {
                    Log($"Ribbon creation failed (non-fatal): {ribbonEx.Message}");
                }

                Log("RevitHarness add-in started successfully");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log($"Error during startup: {ex}");
                TaskDialog.Show("RevitHarness Error", $"Failed to start RevitHarness add-in: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Called when Revit shuts down.
        /// </summary>
        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                Log("RevitHarness add-in shutting down...");

                _dialogHooks?.Unsubscribe();
                _dialogHooks = null;
                BundledDependencyResolver.Uninstall();

                // Stop processing timer
                _processingTimer?.Dispose();
                _processingTimer = null;

                // Stop IPC server
                _server?.Stop();
                _server?.Dispose();
                _server = null;

                // Dispose external event
                _externalEvent?.Dispose();
                _externalEvent = null;

                Log("RevitHarness add-in shut down successfully");
                CloseLogging();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log($"Error during shutdown: {ex}");
                return Result.Failed;
            }
        }

        private void CreateRibbon(UIControlledApplication application)
        {
            application.CreateRibbonTab("RevitHarness");
            var panel = application.CreateRibbonPanel("RevitHarness", "Tools");

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var buttonData = new PushButtonData(
                "OpenAddinManager",
                "Addin\nManager",
                assemblyPath,
                typeof(OpenAddinManagerCommand).FullName);

            panel.AddItem(buttonData);
            Log("Ribbon created: RevitHarness > Tools > Addin Manager");
        }

        private void ProcessPendingRequests(object? state)
        {
            try
            {
                if (_server == null || _externalEvent == null)
                    return;

                if (_server.PendingRequestCount > 0)
                {
                    _externalEvent.Raise();
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing requests: {ex.Message}");
            }
        }

        private static void InitializeLogging()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(localAppData, "RevitHarness", "logs");
                Directory.CreateDirectory(logDir);

                var logPath = Path.Combine(logDir, $"harness_{DateTime.Now:yyyyMMdd}.log");
                _logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
            }
            catch
            {
                // Logging is optional, don't fail if we can't create the log file
            }
        }

        private static void CloseLogging()
        {
            try
            {
                _logWriter?.Close();
                _logWriter?.Dispose();
                _logWriter = null;
            }
            catch
            {
                // Ignore errors when closing log
            }
        }

        private static void Log(string message)
        {
            lock (_logLock)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var threadId = Thread.CurrentThread.ManagedThreadId;
                    var logLine = $"[{timestamp}] [{threadId:D4}] {message}";

                    _logWriter?.WriteLine(logLine);
                    System.Diagnostics.Debug.WriteLine($"[RevitHarness] {logLine}");
                }
                catch
                {
                    // Ignore logging errors
                }
            }
        }
    }
}
