using Microsoft.CodeAnalysis;

namespace EnumClass.Generator
{
    internal sealed record TypeModel(
        string HintName,
        string? Namespace,
        string Name,
        EquatableArray<DataPropertyModel> Properties,
        EquatableArray<MemberModel> Members,
        bool GenerateName,
        bool PhfFold,
        EquatableArray<int> PhfDisplacements, // empty => linear lookup
        EquatableArray<int> PhfSlots,         // slot -> ordinal
        EquatableArray<DiagnosticInfo> Diagnostics,
        bool CanEmit);

    /// <param name="Modifiers">Copied verbatim from the defining declaration (partial members must match).</param>
    internal sealed record DataPropertyModel(string Modifiers, string Type, string Name);

    /// <param name="Values">C# expressions, one per data property, in property order.</param>
    internal sealed record MemberModel(string Modifiers, string Name, EquatableArray<string> Values);

    internal static class Diagnostics
    {
        private const string Category = "EnumClass";

        public static readonly DiagnosticDescriptor InvalidTarget = new(
            "EC001", "Invalid [DataEnum] target",
            "'{0}' must be a non-nested, non-generic 'readonly partial struct'",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidMember = new(
            "EC002", "Invalid member declaration",
            "[Member] '{0}' must be a get-only 'static partial' property of the containing type",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ArityMismatch = new(
            "EC003", "Wrong number of member values",
            "Member '{0}' supplies {1} value(s) but '{2}' declares {3} data propert(y/ies)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TypeMismatch = new(
            "EC004", "Member value has the wrong type",
            "Value {0} for '{1}.{2}' has type '{3}', which has no implicit conversion to '{4}'",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidDataProperty = new(
            "EC005", "Invalid data property",
            "Data property '{0}' must be a get-only instance partial property (no setter, no indexer)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SplitMembers = new(
            "EC006", "Members span several files",
            "Members of '{0}' are declared in more than one file; ordinals follow file-path order, which is fragile",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoMembers = new(
            "EC007", "No members",
            "'{0}' declares no [Member] properties",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CaseCollision = new(
            "EC008", "Members differ only by ASCII case",
            "Members of '{0}' differ only by ASCII case; case-insensitive TryParse falls back to a linear scan and returns the first match",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TooManyMembers = new(
            "EC009", "Too many members",
            "'{0}' declares {1} members; at most 65536 are supported",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
