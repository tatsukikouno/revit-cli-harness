#nullable enable

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitHarness.Addin.Ipc;
using RevitHarness.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace RevitHarness.Addin.Commands
{
    public enum CommandInterfaceType
    {
        HarnessCommand,
        ExternalCommand
    }

    public class CommandExecutionResult
    {
        public bool Success { get; set; }
        public string? ResultText { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
    }

    public class CommandExecutor
    {
        private readonly ShadowCopier _shadowCopier;
        private readonly Action<string> _log;

        public CommandExecutor(ShadowCopier shadowCopier, Action<string> log)
        {
            _shadowCopier = shadowCopier;
            _log = log;
        }

        public CommandExecutionResult ExecuteCommand(
            UIApplication uiapp,
            string assemblyPath,
            string commandType,
            IDictionary<string, object>? args = null)
        {
            args = args ?? new Dictionary<string, object>();
            ResolveEventHandler? resolver = null;

            try
            {
                // Shadow copy the assembly
                _log($"Shadow copying: {assemblyPath}");
                var shadowPath = _shadowCopier.CopyToShadow(assemblyPath);

                // Load the assembly from bytes to bypass .NET assembly cache
                _log($"Loading assembly from bytes (cache bypass)");
                var (assembly, assemblyResolver) = _shadowCopier.LoadAssemblyFromBytes(shadowPath);
                resolver = assemblyResolver;
                AppDomain.CurrentDomain.AssemblyResolve += resolver;

                // Find the command type
                var type = assembly.GetType(commandType);
                if (type == null)
                {
                    return new CommandExecutionResult
                    {
                        Success = false,
                        ErrorMessage = $"Type not found: {commandType}"
                    };
                }

                // Check if type implements IHarnessCommand
                if (typeof(IHarnessCommand).IsAssignableFrom(type))
                {
                    _log("Creating harness command instance");
                    var command = (IHarnessCommand)Activator.CreateInstance(type)!;

                    _log("Executing harness command");
                    var resultJson = command.Execute(uiapp, args);
                    _log("Command completed");

                    _shadowCopier.Cleanup();
                    return new CommandExecutionResult
                    {
                        Success = true,
                        ResultText = resultJson
                    };
                }

                // Fallback: Check if type implements IExternalCommand
                if (typeof(IExternalCommand).IsAssignableFrom(type))
                {
                    _log("Creating IExternalCommand instance");
                    var command = (IExternalCommand)Activator.CreateInstance(type)!;

                    _log("Executing command (note: ExternalCommandData will be null)");
                    var message = string.Empty;
                    var elements = new ElementSet();

                    var result = command.Execute(null!, ref message, elements);
                    _log($"Command completed with result: {result}");

                    _shadowCopier.Cleanup();
                    return new CommandExecutionResult
                    {
                        Success = true,
                        ResultText = string.IsNullOrEmpty(message)
                            ? $"Result: {result}"
                            : $"Result: {result}\n{message}"
                    };
                }

                return new CommandExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Type {commandType} does not implement IHarnessCommand or IExternalCommand"
                };
            }
            catch (Exception ex)
            {
                _log($"Command execution failed: {ex}");
                return new CommandExecutionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.ToString()
                };
            }
            finally
            {
                if (resolver != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= resolver;
                }
            }
        }
    }
}
