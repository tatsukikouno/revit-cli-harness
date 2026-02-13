#nullable enable

using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitHarness.Lookup
{
    /// <summary>
    /// Result of an element inspection.
    /// </summary>
    public class InspectionResult
    {
        [JsonProperty("elementId")]
        public int ElementId { get; set; }

        [JsonProperty("elementName")]
        public string? ElementName { get; set; }

        [JsonProperty("categoryName")]
        public string? CategoryName { get; set; }

        [JsonProperty("typeName")]
        public string? TypeName { get; set; }

        [JsonProperty("typeFullName")]
        public string? TypeFullName { get; set; }

        [JsonProperty("memberCount")]
        public int MemberCount { get; set; }

        [JsonProperty("members")]
        public List<MemberInfo> Members { get; set; } = new List<MemberInfo>();

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public ErrorInfo? ErrorDetail { get; set; }

        public static InspectionResult Error(string code, string message)
        {
            return new InspectionResult
            {
                ErrorDetail = new ErrorInfo { Code = code, Message = message }
            };
        }
    }

    /// <summary>
    /// Information about a decomposed member.
    /// </summary>
    public class MemberInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("declaringType")]
        public string? DeclaringType { get; set; }

        [JsonProperty("memberKind")]
        public string? MemberKind { get; set; }

        [JsonProperty("valueName")]
        public string? ValueName { get; set; }

        [JsonProperty("valueType")]
        public string? ValueType { get; set; }

        [JsonProperty("valueTypeFullName")]
        public string? ValueTypeFullName { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string? Description { get; set; }

        [JsonProperty("depth")]
        public int Depth { get; set; }

        [JsonProperty("computationTimeMs")]
        public double ComputationTimeMs { get; set; }
    }

    /// <summary>
    /// Error detail within an inspection result.
    /// </summary>
    public class ErrorInfo
    {
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }
}
