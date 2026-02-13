#nullable enable

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using LookupEngine;
using LookupEngine.Abstractions;
using LookupEngine.Abstractions.Enums;
using RevitHarness.Lookup.Descriptors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitHarness.Lookup
{
    /// <summary>
    /// Inspects Revit elements using LookupEngine to decompose their properties and methods.
    /// </summary>
    public class ElementInspector
    {
        /// <summary>
        /// Inspect a Revit element by its ElementId.
        /// </summary>
        public InspectionResult Inspect(UIApplication uiapp, int elementId, InspectOptions? options = null)
        {
            options ??= new InspectOptions();

            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return InspectionResult.Error("NO_ACTIVE_DOCUMENT", "No active document is open in Revit.");
            }

            var id = new ElementId((long)elementId);
            var element = doc.GetElement(id);
            if (element == null)
            {
                return InspectionResult.Error("ELEMENT_NOT_FOUND", $"Element with Id {elementId} not found.");
            }

            var decomposeOptions = new DecomposeOptions
            {
                IncludeFields = options.IncludeFields,
                IncludePrivateMembers = options.IncludePrivateMembers,
                IncludeStaticMembers = options.IncludeStaticMembers,
                IncludeRoot = true,
                TypeResolver = DescriptorsMap.FindDescriptor
            };

            var decomposed = LookupComposer.Decompose(element, decomposeOptions);
            var members = MapMembers(decomposed.Members, options);

            return new InspectionResult
            {
                ElementId = elementId,
                ElementName = element.Name,
                CategoryName = element.Category?.Name,
                TypeName = decomposed.TypeName,
                TypeFullName = decomposed.TypeFullName,
                MemberCount = members.Count,
                Members = members
            };
        }

        private static List<MemberInfo> MapMembers(List<DecomposedMember> decomposedMembers, InspectOptions options)
        {
            IEnumerable<DecomposedMember> filtered = decomposedMembers;

            // Filter by depth
            if (options.MaxDepth >= 0)
            {
                filtered = filtered.Where(m => m.Depth <= options.MaxDepth);
            }

            // Filter by name
            if (!string.IsNullOrEmpty(options.NameFilter))
            {
                var filter = options.NameFilter!;
                filtered = filtered.Where(m =>
                    m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Limit count
            if (options.MaxMembers > 0)
            {
                filtered = filtered.Take(options.MaxMembers);
            }

            return filtered.Select(m => new MemberInfo
            {
                Name = m.Name,
                DeclaringType = m.DeclaringTypeName,
                MemberKind = GetMemberKind(m.MemberAttributes),
                ValueName = m.Value.Name,
                ValueType = m.Value.TypeName,
                ValueTypeFullName = m.Value.TypeFullName,
                Description = m.Value.Description,
                Depth = m.Depth,
                ComputationTimeMs = m.ComputationTime
            }).ToList();
        }

        private static string GetMemberKind(MemberAttributes attributes)
        {
            if (attributes.HasFlag(MemberAttributes.Property)) return "Property";
            if (attributes.HasFlag(MemberAttributes.Method)) return "Method";
            if (attributes.HasFlag(MemberAttributes.Field)) return "Field";
            if (attributes.HasFlag(MemberAttributes.Extension)) return "Extension";
            if (attributes.HasFlag(MemberAttributes.Event)) return "Event";
            return "Unknown";
        }
    }
}
