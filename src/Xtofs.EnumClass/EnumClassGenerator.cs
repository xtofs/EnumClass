using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Xtofs.EnumClass.Generator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class EnumClassGenerator : IIncrementalGenerator
    {
        public const string AttributeMetadataName = "Xtofs.EnumClass.EnumClassAttribute";
        public const string MembersPropertyName = "Members";
        public const string TableTypeName = "Table";
        public const string IncludesAttributeNamespace = "Xtofs.EnumClass";
        public const string IncludesAttributeMetadataName = "IncludesAttribute`1";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(static ctx =>
            {
                ctx.AddSource("EnumClass.Attributes.g.cs", RuntimeSources.Attributes);
                ctx.AddSource("EnumClass.PerfectHash.g.cs", RuntimeSources.PerfectHash);
            });

            var models = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => ModelBuilder.Build(ctx, ct));

            // Includes may refer to other [EnumClass] types of this compilation, whose
            // generated members don't exist yet. So all models are collected and each is
            // resolved against the others; per-type outputs stay cached by value equality.
            var resolved = models.Collect().SelectMany(static (all, ct) => Resolver.ResolveAll(all, ct));

            context.RegisterSourceOutput(resolved, static (spc, model) =>
            {
                foreach (var d in model.Diagnostics) spc.ReportDiagnostic(d.ToDiagnostic());
                if (model.CanEmit) spc.AddSource(model.Type.HintName, Emitter.Emit(model));
            });
        }
    }

    internal static class ModelBuilder
    {
        private static readonly SymbolDisplayFormat TypeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        /// <summary>Names the emitter declares inside the struct (besides s_&lt;Property&gt; columns).</summary>
        private static readonly HashSet<string> GeneratedNames = new(StringComparer.Ordinal)
        {
            "Count", "Ordinal", "Values", "Set", "Tag", "ToTag", "FromTag", "FromOrdinal", "TryFromOrdinal",
            "TryParse", "Parse", "TryParseLinear", "Equals", "GetHashCode", "ToString", "CompareTo",
            "GetType", "MemberwiseClone", "Row", "Table", "Members", "GetAt",
            "PhfFold", "PhfDisplacements", "PhfSlots", "s_names", "s_values", "s_table", "_ordinal",
        };

        public static TypeModel Build(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var type = (INamedTypeSymbol)ctx.TargetSymbol;
            var diags = new List<DiagnosticInfo>();
            var typeLoc = type.Locations.FirstOrDefault();
            string? ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
            string hint = (ns is null ? "" : ns + ".") + type.Name + ".EnumClass.g.cs";

            string fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var includes = new List<IncludeModel>();

            TypeModel Result(bool canEmit,
                             List<DataPropertyModel>? props = null, List<MemberModel>? members = null,
                             bool generateName = false, List<string>? usings = null) =>
                new(hint, ns, type.Name, fullName, LocationInfo.From(typeLoc), EquatableArray.From(usings ?? new()),
                    EquatableArray.From(props ?? new()), EquatableArray.From(members ?? new()),
                    EquatableArray.From(includes),
                    EquatableArray.From(type.MemberNames.OrderBy(x => x, StringComparer.Ordinal)),
                    generateName,
                    EquatableArray.From(diags),
                    canEmit);

            if (type.TypeKind != TypeKind.Struct || !type.IsReadOnly || type.IsRefLikeType ||
                type.ContainingType is not null || type.IsGenericType)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidTarget, typeLoc, type.Name));
                return Result(false);
            }

            // ---- data properties: bodiless instance partial properties --------------
            var dataProps = new List<IPropertySymbol>();
            foreach (var symbol in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();
                if (symbol is not IPropertySymbol p || p.IsStatic ||
                    !p.IsPartialDefinition || p.PartialImplementationPart is not null)
                    continue;

                if (p.SetMethod is not null || p.IsIndexer)
                    diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidDataProperty, p.Locations.FirstOrDefault(), p.Name));
                else
                    dataProps.Add(p);
            }
            // Declaration order defines the Row constructor's parameter order.
            dataProps = dataProps.OrderBy(FilePath, StringComparer.Ordinal).ThenBy(SpanStart).ToList();
            var dataPropNames = new HashSet<string>(dataProps.Select(p => p.Name), StringComparer.Ordinal);

            // ---- [Includes<T>], in attribute order -------------------------------------
            bool includesOk = true;
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass is not { IsGenericType: true } ac ||
                    ac.ConstructedFrom.MetadataName != EnumClassGenerator.IncludesAttributeMetadataName ||
                    ac.ContainingNamespace.ToDisplayString() != EnumClassGenerator.IncludesAttributeNamespace)
                    continue;
                if (ac.TypeArguments[0] is not INamedTypeSymbol included || included.TypeKind == TypeKind.Error)
                {
                    includesOk = false; // the compiler reports it
                    continue;
                }

                var loc = attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? typeLoc;
                string incFull = included.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string incDisplay = included.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                bool isEnumClass = included.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == EnumClassGenerator.AttributeMetadataName);

                if (isEnumClass && !included.DeclaringSyntaxReferences.IsEmpty)
                {
                    includes.Add(new IncludeModel(incFull, incDisplay, included.Name, LocationInfo.From(loc),
                        FromSource: true, EquatableArray.From(Array.Empty<string>())));
                }
                else if (!included.DeclaringSyntaxReferences.IsEmpty)
                {
                    diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidInclude, loc, included.Name, "it is not an [EnumClass] type"));
                    includesOk = false;
                }
                else if (ReadMetadataNames(included) is { } names)
                {
                    includes.Add(new IncludeModel(incFull, incDisplay, included.Name, LocationInfo.From(loc),
                        FromSource: false, EquatableArray.From(names)));
                }
                else
                {
                    diags.Add(DiagnosticInfo.Create(Diagnostics.InvalidInclude, loc, included.Name,
                        "it does not have the shape of a generated EnumClass type (Tag, Count, FromOrdinal, Set.GetWord/FromWords)"));
                    includesOk = false;
                }
            }

            if (includes.Count > 0 && dataProps.Count > 0)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.DataWithIncludes, typeLoc, type.Name));
                includesOk = false;
            }

            // ---- Members: purely syntactic, since the generated Table type does not exist yet --
            var dataDecls = type.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(ct))
                .OfType<TypeDeclarationSyntax>()
                .SelectMany(t => t.Members)
                .Where(m => m switch
                {
                    PropertyDeclarationSyntax p => p.Identifier.ValueText == EnumClassGenerator.MembersPropertyName,
                    MethodDeclarationSyntax md => md.Identifier.ValueText == EnumClassGenerator.MembersPropertyName,
                    FieldDeclarationSyntax f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == EnumClassGenerator.MembersPropertyName),
                    _ => false,
                })
                .ToList();

            if (dataDecls.Count == 0)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.MissingData, typeLoc, type.Name));
                return Result(false);
            }

            if (dataDecls.FirstOrDefault(d => d is PropertyDeclarationSyntax) is not PropertyDeclarationSyntax impl)
            {
                var other = dataDecls[0];
                diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedDataShape, other.GetLocation(),
                    other is FieldDeclarationSyntax
                        ? "Members must be a property, not a field: field initializers in different partial declarations run in unspecified order"
                        : "Members must be a property: 'private static Table Members => new() { ... };'"));
                return Result(false);
            }

            if (!impl.Modifiers.Any(SyntaxKind.StaticKeyword) ||
                impl.Type is not IdentifierNameSyntax { Identifier.ValueText: EnumClassGenerator.TableTypeName })
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedDataShape, impl.Identifier.GetLocation(),
                    "Members must be declared as 'private static Table Members => new() { ... };'"));
                return Result(false);
            }

            var initializer = GetObjectInitializer(impl);
            if (initializer is null)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedDataShape, impl.GetLocation(),
                    "Members must return a single object creation with an initializer: '=> new() { Member = new(...), ... }'"));
                return Result(false);
            }

            // ---- members: one per simple assignment, in initializer order ------------
            var memberModels = new List<MemberModel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var expr in initializer.Expressions)
            {
                ct.ThrowIfCancellationRequested();
                if (expr is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax id } assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    diags.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedDataShape, expr.GetLocation(),
                        "Each Members initializer entry must have the form 'Member = <expression>'"));
                    continue;
                }

                string name = id.Identifier.ValueText;
                // A duplicate is reported by the compiler (CS1912); emit it once.
                if (!seen.Add(name)) continue;

                string? conflict =
                    name == type.Name ? "the enclosing type (CS0542)" :
                    dataPropNames.Contains(name) ? $"data property '{name}'" :
                    GeneratedNames.Contains(name) || name.StartsWith("s_", StringComparison.Ordinal) ? "a generated member" :
                    name == "Name" && !type.GetMembers("Name").Any() ? "the generated Name property" :
                    !type.GetMembers(name).IsEmpty ? $"existing member '{name}'" :
                    null;
                if (conflict is not null)
                {
                    diags.Add(DiagnosticInfo.Create(Diagnostics.NameConflict, id.GetLocation(), name, conflict));
                    continue;
                }

                memberModels.Add(new MemberModel(name, EquatableArray.From(DocLines(assignment))));
            }

            if (memberModels.Count == 0)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.NoMembers, initializer.GetLocation(), type.Name));
                return Result(false);
            }
            if (memberModels.Count > 65536)
            {
                diags.Add(DiagnosticInfo.Create(Diagnostics.TooManyMembers, typeLoc, type.Name, memberModels.Count));
                return Result(false);
            }

            // Name defaults to the member identifier unless the user claims "Name" for something.
            bool generateName = type.GetMembers("Name").IsEmpty;

            var propModels = dataProps
                .Select(p => new DataPropertyModel(Modifiers(p, ct), p.Type.ToDisplayString(TypeFormat), p.Name))
                .ToList();

            return Result(includesOk, propModels, memberModels, generateName, UsingsInScope(impl));
        }

        /// <summary>
        /// The values of an EnumClass type from a referenced assembly, read from its public
        /// <c>Tag</c> enum (whose constants are the ordinals); null if T lacks the generated shape.
        /// </summary>
        private static string[]? ReadMetadataNames(INamedTypeSymbol t)
        {
            var tag = t.GetTypeMembers("Tag").FirstOrDefault(x => x.TypeKind == TypeKind.Enum);
            var set = t.GetTypeMembers("Set").FirstOrDefault();
            if (tag is null || set is null ||
                set.GetMembers("GetWord").IsEmpty || set.GetMembers("FromWords").IsEmpty ||
                t.GetMembers("FromOrdinal").IsEmpty)
                return null;

            var fields = tag.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .Select(f => (f.Name, Value: Convert.ToInt64(f.ConstantValue, System.Globalization.CultureInfo.InvariantCulture)))
                .OrderBy(f => f.Value)
                .ToList();
            for (int i = 0; i < fields.Count; i++)
                if (fields[i].Value != i) return null;

            bool countOk = t.GetMembers("Count").OfType<IFieldSymbol>()
                .Any(f => f.IsConst && f.ConstantValue is int c && c == fields.Count);
            return countOk && fields.Count > 0 ? fields.Select(f => f.Name).ToArray() : null;
        }

        /// <summary>
        /// Accepts <c>Members =&gt; new() { ... }</c>, <c>Members { get =&gt; new() { ... }; }</c> and
        /// <c>Members { get { return new() { ... }; } }</c> (also <c>new Table { ... }</c>).
        /// </summary>
        internal static InitializerExpressionSyntax? GetObjectInitializer(PropertyDeclarationSyntax property)
        {
            ExpressionSyntax? expr = property.ExpressionBody?.Expression;
            if (expr is null && property.Initializer is null &&
                property.AccessorList is { Accessors: { Count: 1 } accessors } &&
                accessors[0] is { Keyword.RawKind: (int)SyntaxKind.GetKeyword } getter)
            {
                expr = getter.ExpressionBody?.Expression;
                if (expr is null && getter.Body is { Statements.Count: 1 } body && body.Statements[0] is ReturnStatementSyntax ret)
                    expr = ret.Expression;
            }

            return expr is BaseObjectCreationExpressionSyntax { Initializer: { } init } creation &&
                   init.IsKind(SyntaxKind.ObjectInitializerExpression) &&
                   (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0)
                ? init
                : null;
        }

        /// <summary>
        /// The <c>///</c> lines in front of an initializer entry. Handles both trivia shapes:
        /// structured doc-comment trivia (DocumentationMode.Parse/Diagnose) and plain
        /// single-line comments (DocumentationMode.None).
        /// </summary>
        private static string[] DocLines(AssignmentExpressionSyntax assignment)
        {
            var lines = new List<string>();
            foreach (var trivia in assignment.GetLeadingTrivia())
            {
                if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
                    !trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    continue;

                foreach (var raw in trivia.ToFullString().Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("///", StringComparison.Ordinal) && !line.StartsWith("////", StringComparison.Ordinal))
                        lines.Add(line);
                }
            }
            return lines.ToArray();
        }

        /// <summary>Non-global using directives visible at <paramref name="node"/>, outermost first.</summary>
        private static List<string> UsingsInScope(SyntaxNode node)
        {
            var result = new List<string>();
            foreach (var scope in node.Ancestors().Reverse())
            {
                var usings = scope switch
                {
                    CompilationUnitSyntax cu => cu.Usings,
                    BaseNamespaceDeclarationSyntax nsDecl => nsDecl.Usings,
                    _ => default,
                };
                foreach (var u in usings)
                    if (u.GlobalKeyword.IsKind(SyntaxKind.None))
                        result.Add(u.WithoutTrivia().ToString());
            }
            return result;
        }

        private static string FilePath(ISymbol s) => s.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "";
        private static int SpanStart(ISymbol s) => s.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;

        private static string Modifiers(IPropertySymbol p, CancellationToken ct) =>
            p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is PropertyDeclarationSyntax decl
                ? string.Join(" ", decl.Modifiers.Select(t => t.Text))
                : "public partial";
    }
}
