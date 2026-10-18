using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EnumClass.Generator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class DataEnumGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(static ctx =>
            {
                ctx.AddSource("EnumClass.Attributes.g.cs", RuntimeSources.Attributes);
                ctx.AddSource("EnumClass.PerfectHash.g.cs", RuntimeSources.PerfectHash);
            });

            var models = context.SyntaxProvider.ForAttributeWithMetadataName(
                "EnumClass.EnumClassAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => ModelBuilder.Build(ctx, ct));

            context.RegisterSourceOutput(models, static (spc, model) =>
            {
                foreach (var d in model.Diagnostics) spc.ReportDiagnostic(d.ToDiagnostic());
                if (model.CanEmit) spc.AddSource(model.HintName, Emitter.Emit(model));
            });
        }
    }

    internal static class ModelBuilder
    {
        private static readonly SymbolDisplayFormat TypeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        public static TypeModel Build(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var type = (INamedTypeSymbol)ctx.TargetSymbol;
            var compilation = ctx.SemanticModel.Compilation;
            var diags = new List<DiagnosticInfo>();
            var typeLoc = type.Locations.FirstOrDefault();
            string? ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
            string hint = (ns is null ? "" : ns + ".") + type.Name + ".DataEnum.g.cs";

            TypeModel Result(bool canEmit,
                             List<DataPropertyModel>? props = null, List<MemberModel>? members = null,
                             bool generateName = false, bool fold = false,
                             int[]? g = null, int[]? slots = null) =>
                new(hint, ns, type.Name,
                    EquatableArray.From(props ?? new()), EquatableArray.From(members ?? new()),
                    generateName, fold,
                    new EquatableArray<int>(g ?? Array.Empty<int>()),
                    new EquatableArray<int>(slots ?? Array.Empty<int>()),
                    EquatableArray.From(diags),
                    canEmit && !diags.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error));

            if (type.TypeKind != TypeKind.Struct || !type.IsReadOnly || type.IsRefLikeType ||
                type.ContainingType is not null || type.IsGenericType)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidTarget, typeLoc, type.Name));
                return Result(false);
            }

            var memberAttr = compilation.GetTypeByMetadataName("EnumClass.MemberAttribute");
            var dataProps = new List<IPropertySymbol>();
            var members = new List<(IPropertySymbol Prop, AttributeData Attr)>();

            foreach (var symbol in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();
                // Only bodiless partial definitions are ours to implement.
                if (symbol is not IPropertySymbol p || !p.IsPartialDefinition || p.PartialImplementationPart is not null)
                    continue;

                var attr = p.GetAttributes().FirstOrDefault(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, memberAttr));
                var loc = p.Locations.FirstOrDefault();

                if (attr is not null)
                {
                    if (!p.IsStatic || p.SetMethod is not null ||
                        !SymbolEqualityComparer.Default.Equals(p.Type, type))
                        diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidMember, loc, p.Name));
                    else
                        members.Add((p, attr));
                }
                else if (!p.IsStatic)
                {
                    if (p.SetMethod is not null || p.IsIndexer)
                        diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidDataProperty, loc, p.Name));
                    else
                        dataProps.Add(p);
                }
            }

            // Declaration order defines ordinals and positional binding.
            dataProps = dataProps.OrderBy(FilePath, StringComparer.Ordinal).ThenBy(SpanStart).ToList();
            members = members.OrderBy(m => FilePath(m.Prop), StringComparer.Ordinal).ThenBy(m => SpanStart(m.Prop)).ToList();

            if (members.Count == 0)
                diags.Add(DiagnosticInfo.Create(Diagnostics.NoMembers, typeLoc, type.Name));
            if (members.Count > 65536)
                diags.Add(DiagnosticInfo.Create(Diagnostics.TooManyMembers, typeLoc, type.Name, members.Count));
            if (members.Select(m => FilePath(m.Prop)).Distinct().Count() > 1)
                diags.Add(DiagnosticInfo.Create(Diagnostics.SplitMembers, typeLoc, type.Name));

            bool sawCompilerError = false;
            var memberModels = new List<MemberModel>(members.Count);
            foreach (var (prop, attr) in members)
            {
                ct.ThrowIfCancellationRequested();
                var attrLoc = attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? prop.Locations.FirstOrDefault();
                var values = ExtractValues(attr, ref sawCompilerError);

                if (values.Length != dataProps.Count)
                {
                    diags.Add(DiagnosticInfo.Create(Diagnostics.ArityMismatch, attrLoc,
                        prop.Name, values.Length, type.Name, dataProps.Count));
                    continue;
                }

                var literals = new string[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    var v = values[i];
                    var target = dataProps[i].Type;
                    if (v.Kind == TypedConstantKind.Error) { sawCompilerError = true; continue; }
                    if (!IsAssignable(compilation, v, target))
                    {
                        diags.Add(DiagnosticInfo.Create(Diagnostics.TypeMismatch, attrLoc,
                            v.IsNull ? "null" : v.ToCSharpString(), type.Name, dataProps[i].Name,
                            v.Type?.ToDisplayString() ?? "null", target.ToDisplayString()));
                        continue;
                    }
                    literals[i] = Literal(v);
                }

                memberModels.Add(new MemberModel(Modifiers(prop, ct), prop.Name, new EquatableArray<string>(literals)));
            }

            // Name defaults to the member identifier unless the user claims "Name" for something.
            bool generateName = type.GetMembers("Name").IsEmpty;

            // Perfect hash over member identifiers, computed here so it is cached with the model.
            var keys = memberModels.Select(m => m.Name).ToArray();
            bool fold = true;
            int[]? g = null, slots = null;
            if (!PerfectHashBuilder.TryBuild(keys, fold: true, out var g1, out var s1))
            {
                fold = false;
                if (keys.Select(PerfectHashBuilder.FoldAscii).Distinct().Count() != keys.Length)
                    diags.Add(DiagnosticInfo.Create(Diagnostics.CaseCollision, typeLoc, type.Name));
                if (PerfectHashBuilder.TryBuild(keys, fold: false, out var g2, out var s2)) { g = g2; slots = s2; }
            }
            else { g = g1; slots = s1; }

            var propModels = dataProps
                .Select(p => new DataPropertyModel(Modifiers(p, ct), p.Type.ToDisplayString(TypeFormat), p.Name))
                .ToList();

            return Result(!sawCompilerError, propModels, memberModels, generateName, fold, g, slots);
        }

        private static string FilePath(ISymbol s) => s.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "";
        private static int SpanStart(ISymbol s) => s.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;

        private static string Modifiers(IPropertySymbol p, CancellationToken ct) =>
            p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is PropertyDeclarationSyntax decl
                ? string.Join(" ", decl.Modifiers.Select(t => t.Text))
                : (p.IsStatic ? "public static partial" : "public partial");

        private static ImmutableArray<TypedConstant> ExtractValues(AttributeData attr, ref bool sawError)
        {
            var args = attr.ConstructorArguments;
            if (args.Length != 1) { sawError = true; return ImmutableArray<TypedConstant>.Empty; }
            var a = args[0];
            if (a.Kind == TypedConstantKind.Error) { sawError = true; return ImmutableArray<TypedConstant>.Empty; }
            // [Member(null)] binds in normal form: the params array itself is null.
            // We read that as "one null value", which is what the author almost certainly meant.
            if (a.IsNull) return ImmutableArray.Create(a);
            return a.Kind == TypedConstantKind.Array ? a.Values : ImmutableArray.Create(a);
        }

        private static bool IsAssignable(Compilation c, TypedConstant v, ITypeSymbol target)
        {
            if (v.IsNull)
                return target.IsReferenceType || target.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            // Note: no constant conversions here — an int literal does not convert to byte.
            return v.Type is not null && c.ClassifyCommonConversion(v.Type, target).IsImplicit;
        }

        private static string Literal(TypedConstant v)
        {
            if (v.IsNull) return "null";
            switch (v.Kind)
            {
                case TypedConstantKind.Array:
                    var elem = ((IArrayTypeSymbol)v.Type!).ElementType.ToDisplayString(TypeFormat);
                    return $"new {elem}[] {{ {string.Join(", ", v.Values.Select(Literal))} }}";
                case TypedConstantKind.Type:
                    return $"typeof({((ITypeSymbol)v.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
                case TypedConstantKind.Enum:
                    return $"({v.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({Primitive(v.Value!)})";
                default:
                    return Primitive(v.Value!);
            }
        }

        private static string Inv(IFormattable f, string? format = null) => f.ToString(format, CultureInfo.InvariantCulture);

        private static string Primitive(object value) => value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char ch => SymbolDisplay.FormatLiteral(ch, quote: true),
            bool b => b ? "true" : "false",
            int i => Inv(i),
            uint u => Inv(u) + "U",
            long l => l == long.MinValue ? "long.MinValue" : Inv(l) + "L",
            ulong ul => Inv(ul) + "UL",
            short s16 => $"(short)({Inv(s16)})",   // keyword casts: (short)-1 would parse fine,
            ushort u16 => $"(ushort)({Inv(u16)})", // but (global::System.Int16)-1 would not
            byte u8 => $"(byte)({Inv(u8)})",
            sbyte s8 => $"(sbyte)({Inv(s8)})",
            float f => FormatFloat(f),
            double d => FormatDouble(d),
            _ => throw new NotSupportedException("Unsupported attribute constant: " + value.GetType()),
        };

        private static string FormatDouble(double d)
        {
            if (double.IsNaN(d)) return "double.NaN";
            if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
            // "R" is not reliably round-trip on .NET Framework (the VS compiler host); verify, else G17.
            string r = Inv(d, "R");
            return (double.Parse(r, CultureInfo.InvariantCulture) == d ? r : Inv(d, "G17")) + "D";
        }

        private static string FormatFloat(float f)
        {
            if (float.IsNaN(f)) return "float.NaN";
            if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
            if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
            string r = Inv(f, "R");
            return (float.Parse(r, CultureInfo.InvariantCulture) == f ? r : Inv(f, "G9")) + "F";
        }
    }
}
