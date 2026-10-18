using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Xtofs.EnumClass.Generator
{
    /// <summary>
    /// Suppresses CS1587 ("XML comment is not placed on a valid language element") for
    /// <c>///</c> comments on entries of an [EnumClass] Members initializer. The generator
    /// copies those comments onto the generated member properties, so they are not lost.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DocCommentSuppressor : DiagnosticSuppressor
    {
        internal static readonly SuppressionDescriptor Rule = new(
            id: "ECSPR001",
            suppressedDiagnosticId: "CS1587",
            justification: "Doc comments on EnumClass Members entries are copied to the generated members.");

        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(Rule);

        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            var ct = context.CancellationToken;
            foreach (var diagnostic in context.ReportedDiagnostics)
            {
                var tree = diagnostic.Location.SourceTree;
                if (tree is null) continue;

                // The diagnostic points into the doc-comment trivia; the token that trivia
                // leads is the member identifier on the left of an initializer assignment.
                var trivia = tree.GetRoot(ct).FindTrivia(diagnostic.Location.SourceSpan.Start);
                if (trivia.Token.Parent is not IdentifierNameSyntax id ||
                    id.Parent is not AssignmentExpressionSyntax assignment ||
                    assignment.Left != id ||
                    assignment.Parent is not InitializerExpressionSyntax initializer)
                    continue;

                var property = initializer.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
                if (property is null ||
                    property.Identifier.ValueText != EnumClassGenerator.MembersPropertyName ||
                    ModelBuilder.GetObjectInitializer(property) != initializer ||
                    property.Parent is not TypeDeclarationSyntax typeDecl)
                    continue;

                var model = context.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol type ||
                    !type.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == EnumClassGenerator.AttributeMetadataName))
                    continue;

                context.ReportSuppression(Suppression.Create(Rule, diagnostic));
            }
        }
    }
}
