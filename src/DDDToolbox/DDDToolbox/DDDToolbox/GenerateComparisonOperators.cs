using System.Collections.Generic;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using DDDToolbox.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace DDDToolbox
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(GenerateComparisonOperators)), Shared]
    internal class GenerateComparisonOperators : CodeRefactoringProvider
    {
        public const string Title = "Generate comparison operators";
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            if (node is PropertyDeclarationSyntax propertyDeclaration)
            {
                context.RegisterRefactoring(CodeAction.Create(title: Title, createChangedDocument: c => AddComparisonOperators(context.Document, propertyDeclaration, c), equivalenceKey: Title));
            }
        }

        private async Task<Document> AddComparisonOperators(Document document, PropertyDeclarationSyntax propertyDeclaration, CancellationToken cancellationToken)
        {
            var typeDeclaration = propertyDeclaration.Parent as TypeDeclarationSyntax;
            var generator = SyntaxGenerator.GetGenerator(document);
            var parameters = new List<SyntaxNode>
            {
                generator.ParameterDeclaration("a", SyntaxFactory.ParseTypeName(typeDeclaration.Identifier.Text)),
                generator.ParameterDeclaration("b", SyntaxFactory.ParseTypeName(typeDeclaration.Identifier.Text))
            };

            var firstValue = generator.MemberAccessExpression(generator.IdentifierName("a"), propertyDeclaration.Identifier.Text);
            var secondValue = generator.MemberAccessExpression(generator.IdentifierName("b"), propertyDeclaration.Identifier.Text);
            var operatorReturnType = SyntaxFactory.ParseTypeName("bool");
            var staticModifiers = new DeclarationModifiers().WithIsStatic(true);

            MemberDeclarationSyntax CreateOperator(OperatorKind kind, SyntaxNode expression)
            {
                var operatorDeclaration = (OperatorDeclarationSyntax) generator.OperatorDeclaration(kind, parameters, operatorReturnType, Accessibility.Public, staticModifiers);
                var arrowExpressionClause = SyntaxFactory.ArrowExpressionClause((ExpressionSyntax)expression);
                return operatorDeclaration.WithExpressionBody(arrowExpressionClause)
                    .WithBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
            };


            var newOperators = new[]
            {
                CreateOperator(OperatorKind.Equality, generator.ValueEqualsExpression(firstValue, secondValue)),
                CreateOperator(OperatorKind.Inequality, generator.ValueNotEqualsExpression(firstValue, secondValue)),
                CreateOperator(OperatorKind.GreaterThan, generator.GreaterThanExpression(firstValue, secondValue)),
                CreateOperator(OperatorKind.LessThan, generator.LessThanExpression(firstValue, secondValue)),
                CreateOperator(OperatorKind.GreaterThanOrEqual, generator.GreaterThanOrEqualExpression(firstValue, secondValue)),
                CreateOperator(OperatorKind.LessThanOrEqual, generator.LessThanOrEqualExpression(firstValue, secondValue))
            };
            var newClassDeclaration =  typeDeclaration.WithMembers(typeDeclaration.Members.AddRange(newOperators));
            return await document.ReplaceNodes(typeDeclaration, newClassDeclaration, cancellationToken);
        }
    }
}
