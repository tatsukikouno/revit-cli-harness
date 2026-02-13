#nullable enable

namespace RevitHarness.Lookup
{
    /// <summary>
    /// Options for element inspection.
    /// </summary>
    public class InspectOptions
    {
        /// <summary>
        /// Maximum decomposition depth (0 = top-level members only).
        /// </summary>
        public int MaxDepth { get; set; } = 0;

        /// <summary>
        /// Maximum number of members to return.
        /// </summary>
        public int MaxMembers { get; set; } = 200;

        /// <summary>
        /// Include field members in decomposition.
        /// </summary>
        public bool IncludeFields { get; set; }

        /// <summary>
        /// Include private members in decomposition.
        /// </summary>
        public bool IncludePrivateMembers { get; set; }

        /// <summary>
        /// Include static members in decomposition.
        /// </summary>
        public bool IncludeStaticMembers { get; set; }

        /// <summary>
        /// Filter members by name (case-insensitive substring match).
        /// </summary>
        public string? NameFilter { get; set; }
    }
}
