#nullable enable

using System;
using System.Reflection;

namespace RevitHarness.Addin
{
    /// <summary>
    /// Shared reflection utilities for handling assembly identity mismatches.
    /// When the same assembly (e.g., RevitHarness.Contracts) is loaded in multiple contexts,
    /// .NET considers the types as different identities, causing IsAssignableFrom to return false.
    /// Name-based checks avoid this issue.
    /// </summary>
    internal static class ReflectionHelper
    {
        /// <summary>
        /// Checks if a type implements an interface by comparing full names.
        /// This avoids false negatives from .NET type identity mismatches that occur when
        /// the same assembly is loaded in multiple contexts.
        /// </summary>
        public static bool ImplementsInterfaceByName(Type type, string interfaceFullName)
        {
            return Array.Exists(type.GetInterfaces(), i => i.FullName == interfaceFullName);
        }

        /// <summary>
        /// Finds a method on a type by name and parameter types, falling back to name-only search.
        /// The fallback handles cases where parameter types have different assembly identities
        /// (e.g., IDictionary from different load contexts).
        /// </summary>
        public static MethodInfo? FindMethod(Type type, string methodName, Type[] parameterTypes)
        {
            // Try exact match first
            var method = type.GetMethod(methodName, parameterTypes);
            if (method != null)
                return method;

            // Fallback: match by name and parameter count, then verify parameter type names
            foreach (var candidate in type.GetMethods())
            {
                if (candidate.Name != methodName)
                    continue;

                var parameters = candidate.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                var match = true;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType.FullName != parameterTypes[i].FullName)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return candidate;
            }

            return null;
        }
    }
}
