#nullable enable

using Autodesk.Revit.UI;
using RevitHarness.Addin.Commands;
using RevitHarness.Addin.Ipc;
using System;
using System.Collections.Concurrent;

namespace RevitHarness.Addin.UI
{
    public class AddinManagerRequest
    {
        public string AssemblyPath { get; set; } = string.Empty;
        public string CommandType { get; set; } = string.Empty;
        public Action<CommandExecutionResult>? Callback { get; set; }
    }

    public class AddinManagerEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<AddinManagerRequest> _queue = new ConcurrentQueue<AddinManagerRequest>();
        private readonly CommandExecutor _executor;

        public AddinManagerEventHandler(Action<string> log)
        {
            var shadowCopier = new ShadowCopier(log);
            _executor = new CommandExecutor(shadowCopier, log);
        }

        public void Enqueue(AddinManagerRequest request)
        {
            _queue.Enqueue(request);
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var request))
            {
                CommandExecutionResult result;
                try
                {
                    result = _executor.ExecuteCommand(app, request.AssemblyPath, request.CommandType);
                }
                catch (Exception ex)
                {
                    result = new CommandExecutionResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.ToString()
                    };
                }

                request.Callback?.Invoke(result);
            }
        }

        public string GetName() => "AddinManagerEventHandler";
    }
}
