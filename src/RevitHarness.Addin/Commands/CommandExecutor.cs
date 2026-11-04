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
        private const string HarnessCommandInterfaceName = "RevitHarness.Contracts.IHarnessCommand";
        private const string ExternalCommandInterfaceName = "Autodesk.Revit.UI.IExternalCommand";

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

                // Check if type implements IHarnessCommand.
                // Use name-based check first because Assembly.Load(byte[]) can cause type identity
                // mismatches when RevitHarness.Contracts is loaded in multiple assembly contexts
                // (e.g., both the harness addin and another addin load their own copy).
                if (ReflectionHelper.ImplementsInterfaceByName(type, HarnessCommandInterfaceName))
                {
                    _log("Creating harness command instance (IHarnessCommand)");
                    var instance = Activator.CreateInstance(type)!;

                    // Try direct cast first (works when assembly identity matches)
                    if (instance is IHarnessCommand command)
                    {
                        _log("Executing harness command (direct)");
                        var resultJson = command.Execute(uiapp, args);
                        _log("Command completed");

                        _shadowCopier.Cleanup();
                        return new CommandExecutionResult
                        {
                            Success = true,
                            ResultText = resultJson
                        };
                    }

                    // Fallback: invoke via reflection to handle cross-context type identity mismatch
                    _log("Direct cast failed (assembly context mismatch), using reflection");
                    var executeMethod = ReflectionHelper.FindMethod(
                        type,
                        "Execute",
                        new[] { typeof(UIApplication), typeof(IDictionary<string, object>) });

                    if (executeMethod != null)
                    {
                        _log("Executing harness command (reflection)");
                        var resultJson = (string?)executeMethod.Invoke(instance, new object[] { uiapp, args });
                        _log("Command completed");

                        _shadowCopier.Cleanup();
                        return new CommandExecutionResult
                        {
                            Success = true,
                            ResultText = resultJson
                        };
                    }

                    return new CommandExecutionResult
                    {
                        Success = false,
                        ErrorMessage = $"Type {commandType} declares IHarnessCommand but Execute method not found"
                    };
                }

                // Fallback: Check if type implements IExternalCommand (name-based for consistency)
                if (ReflectionHelper.ImplementsInterfaceByName(type, ExternalCommandInterfaceName))
                {
                    _log("Creating IExternalCommand instance");
                    var command = (IExternalCommand)Activator.CreateInstance(type)!;

                    _log("Executing IExternalCommand");
                    var message = string.Empty;
                    var elements = new ElementSet();

                    var result = command.Execute(ExternalCommandDataFactory.Create(uiapp), ref message, elements);
                    _log($"Command completed with result: {result}");

                    _shadowCopier.Cleanup();
                    var resultText = string.IsNullOrEmpty(message)
                        ? $"Result: {result}"
                        : $"Result: {result}\n{message}";
                    return new CommandExecutionResult
                    {
                        Success = result == Result.Succeeded,
                        ResultText = resultText,
                        ErrorMessage = result == Result.Succeeded ? null : resultText
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
