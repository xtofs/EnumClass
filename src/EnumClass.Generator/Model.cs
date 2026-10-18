using Microsoft.CodeAnalysis;

namespace EnumClass.Generator
{
    /// <summary>What one [EnumClass] declaration says on its own (before includes are resolved).</summary>
    internal sealed record TypeModel(
        string HintName,
        string? Namespace,
        string Name,
        string FullName,                      // global::-qualified, key for resolving includes
        LocationInfo? Location,
        EquatableArray<string> Usings,        // non-global usings in scope of Data, for doc-comment crefs
        EquatableArray<DataPropertyModel> Properties,
        EquatableArray<MemberModel> Members,  // own values only
        EquatableArray<IncludeModel> Includes,
        EquatableArray<string> ExistingMemberNames,
        bool GenerateName,
        EquatableArray<DiagnosticInfo> Diagnostics,
        bool CanEmit);

    /// <param name="Modifiers">Copied verbatim from the defining declaration (partial members must match).</param>
    internal sealed record DataPropertyModel(string Modifiers, string Type, string Name);

    /// <param name="DocLines">The member's <c>///</c> lines from the Data initializer, trimmed, without indentation.</param>
    internal sealed record MemberModel(string Name, EquatableArray<string> DocLines);

    /// <summary>One <c>[Includes&lt;T&gt;]</c>.</summary>
    /// <param name="FromSource">T is an [EnumClass] in this compilation; its values come from its own model.</param>
    /// <param name="Names">For T from metadata: its values, read from the public <c>T.Tag</c> enum.</param>
    internal sealed record IncludeModel(
        string FullName, string DisplayName, string SimpleName, LocationInfo? Location,
        bool FromSource, EquatableArray<string> Names);

    /// <summary>An include with its block of ordinals in the including type.</summary>
    internal sealed record ResolvedInclude(string FullName, string DisplayName, string SimpleName, int Offset, int Count);

    /// <summary>Everything the emitter needs: own values first, then each included block.</summary>
    internal sealed record EmitModel(
        TypeModel Type,
        EquatableArray<string> AllNames,
        EquatableArray<ResolvedInclude> Includes,
        bool PhfFold,
        EquatableArray<int> PhfDisplacements, // empty => linear lookup
        EquatableArray<int> PhfSlots,         // slot -> ordinal
        EquatableArray<DiagnosticInfo> Diagnostics,
        bool CanEmit);

    internal static class Diagnostics
    {
        private const string Category = "Design";

        public static readonly DiagnosticDescriptor InvalidTarget = new(
            "EC001", "Invalid [EnumClass] target",
            "'{0}' must be a non-nested, non-generic 'readonly partial struct'",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidDataProperty = new(
            "EC005", "Invalid data property",
            "Data property '{0}' must be a get-only instance partial property (no setter, no indexer)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoMembers = new(
            "EC007", "No members",
            "The Data initializer of '{0}' assigns no members",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CaseCollision = new(
            "EC008", "Members differ only by ASCII case",
            "Values of '{0}' differ only by ASCII case; case-insensitive TryParse falls back to a linear scan and returns the first match",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TooManyMembers = new(
            "EC009", "Too many members",
            "'{0}' has {1} values; at most 65536 are supported",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingData = new(
            "EC010", "Missing Data property",
            "'{0}' must declare 'private static Table Data => new() {{ Member = new(...), ... }};'",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedDataShape = new(
            "EC011", "Unsupported Data shape",
            "{0}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NameConflict = new(
            "EC012", "Member name conflicts with another member",
            "Member name '{0}' conflicts with {1}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateValueName = new(
            "EC013", "Duplicate value name",
            "Value name '{0}' occurs more than once in '{1}' ({2}); names must be unique for Tag, Name and TryParse",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidInclude = new(
            "EC014", "Invalid include",
            "'{0}' cannot be included: {1}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DataWithIncludes = new(
            "EC015", "Data properties with includes",
            "'{0}' has [Includes<...>] and data properties; included values cannot supply data, so data properties are not supported on types with includes",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
