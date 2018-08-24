using System.Collections.Generic;
using System.Composition;
using System.Linq;
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
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(AddStructuralEqualityRefactoring)), Shared]
    internal class AddStructuralEqualityRefactoring : CodeRefactoringProvider
    {
        public const string Title = "Add structural equality";

        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            if (node is ClassDeclarationSyntax || node is StructDeclarationSyntax)
            {
                context.RegisterRefactoring(CodeAction.Create(title: Title, createChangedDocument: c => AddStructuralEquality(context.Document, node as TypeDeclarationSyntax, c), equivalenceKey: Title));
            }
        }

        private async Task<Document> AddStructuralEquality(Document document, TypeDeclarationSyntax classDeclaration, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var generator = SyntaxGenerator.GetGenerator(document);
            var newClassDeclaration = ExtendsWithStructuralEquality(classDeclaration, generator, semanticModel);
            return await document.ReplaceNodes(classDeclaration, newClassDeclaration, cancellationToken);
        }

        public static TypeDeclarationSyntax ExtendsWithStructuralEquality(TypeDeclarationSyntax typeDeclaration, SyntaxGenerator generator, SemanticModel semanticModel, TypeDeclarationSyntax originalClassDeclaration = null)
        {
            originalClassDeclaration = originalClassDeclaration ?? typeDeclaration;
            var className = typeDeclaration.Identifier.Text;
            var publicProperties = GetPublicProperties(originalClassDeclaration);

            var newClassDeclaration = typeDeclaration.WithMembers(typeDeclaration.Members.AddRange(new[]
            {
                GenerateEqualsMethod(generator, publicProperties, typeDeclaration),
                GenerateEqualsWithObjMethod(generator, typeDeclaration),
                GenerateGetHashCodeMethod(generator, semanticModel, publicProperties)
            }));
            var eqInterface = SyntaxFactory.ParseTypeName($"System.IEquatable<{className}>");
            return generator.AddBaseType(newClassDeclaration, eqInterface) as TypeDeclarationSyntax;
        }

        private static List<PropertyDeclarationSyntax> GetPublicProperties(TypeDeclarationSyntax originalClassDeclaration)
        {
            return originalClassDeclaration.Members
                .Where(x => x is PropertyDeclarationSyntax pd && pd.Modifiers.Any(SyntaxKind.PublicKeyword))
                .OfType<PropertyDeclarationSyntax>().ToList();
        }

        private static MethodDeclarationSyntax GenerateGetHashCodeMethod(SyntaxGenerator generator, SemanticModel semanticModel, List<PropertyDeclarationSyntax> publicProperties)
        {
            var hashCodeVariable = generator.IdentifierName("hashCode");
            var hasCodeDeclaration = generator.LocalDeclarationStatement("hashCode", generator.LiteralExpression(17));
            var newMethodName = SyntaxFactory.IdentifierName("GetHashCode");

            var calculateHashCode = publicProperties.Select(x =>
            {
                var typeInfo = semanticModel.GetTypeInfo(x.Type);
                if (typeInfo.Type?.IsReferenceType == true)
                {
                    return generator.AssignmentStatement(hashCodeVariable, 
                        generator.AddExpression(
                            generator.MultiplyExpression(hashCodeVariable, generator.LiteralExpression(23)),
                            generator.CoalesceExpression(
                                    generator.InvocationExpression(
                                        SyntaxFactory.ConditionalAccessExpression(
                                            generator.IdentifierName(x.Identifier.Text) as ExpressionSyntax,
                                            SyntaxFactory.MemberBindingExpression(newMethodName))), 
                                        generator.LiteralExpression(0))));
                }

                return generator.AssignmentStatement(hashCodeVariable, 
                    generator.AddExpression(
                        generator.MultiplyExpression(hashCodeVariable, generator.LiteralExpression(23)),
                        generator.InvocationExpression(
                            generator.MemberAccessExpression(generator.IdentifierName(x.Identifier.Text),newMethodName)
                    )));
            });
            var calculateHashCodeStms = new List<SyntaxNode>()
            {
                hasCodeDeclaration,
            };
            foreach (var node in calculateHashCode)
            {
                calculateHashCodeStms.Add(node);
            }

            calculateHashCodeStms.Add(generator.ReturnStatement(hashCodeVariable));

            var getHashCode = generator.MethodDeclaration("GetHashCode",
                returnType: SyntaxFactory.ParseTypeName("int"), accessibility: Accessibility.Public,
                statements: calculateHashCodeStms, modifiers: new DeclarationModifiers().WithIsOverride(true));
            return getHashCode as MethodDeclarationSyntax;
        }

        private static MemberDeclarationSyntax GenerateEqualsWithObjMethod(SyntaxGenerator generator, TypeDeclarationSyntax typeDeclaration)
        {
            var className = typeDeclaration.Identifier.Text;
            var otherObjIdentifier = generator.IdentifierName("other");
            var thisType = SyntaxFactory.ParseTypeName(className);
            var statement = typeDeclaration is StructDeclarationSyntax
                ? generator.ConditionalExpression(
                        generator.IsTypeExpression(otherObjIdentifier, thisType),
                        generator.InvocationExpression(generator.IdentifierName("Equals"), generator.CastExpression(thisType, otherObjIdentifier)),
                        generator.FalseLiteralExpression()
                    )
                : generator.InvocationExpression(
                    generator.IdentifierName("Equals"),
                    generator.TryCastExpression(otherObjIdentifier, thisType)
                    );


            return generator.MethodDeclaration("Equals",
                new[] {generator.ParameterDeclaration("other", SyntaxFactory.ParseTypeName("object"))},
                returnType: SyntaxFactory.ParseTypeName("bool"), accessibility: Accessibility.Public, statements: new[]
                {
                    generator.ReturnStatement(statement)
                }, modifiers: new DeclarationModifiers().WithIsOverride(true)) as MethodDeclarationSyntax;
        }

        private static MethodDeclarationSyntax GenerateEqualsMethod(SyntaxGenerator generator, List<PropertyDeclarationSyntax> publicProperties, TypeDeclarationSyntax typeDeclaration)
        {
            var className = typeDeclaration.Identifier.Text;
            var otherObj = generator.IdentifierName("other");
            var thisObj = generator.ThisExpression();
            var compareWithNull = generator.ValueNotEqualsExpression(otherObj, generator.NullLiteralExpression());
            var compareStatements = publicProperties.Select(p =>
                generator.InvocationExpression(generator.IdentifierName("Equals"),
                    generator.MemberAccessExpression(thisObj, p.Identifier.Text),
                    generator.MemberAccessExpression(otherObj, p.Identifier.Text))/*.WithLeadingTrivia(SyntaxTriviaList.Create(SyntaxFactory.EndOfLine(Environment.NewLine)))*/).ToList();

            var compareStatement = GetCompareStatement(generator, typeDeclaration, compareStatements, compareWithNull);

            var equalsMethod = generator.MethodDeclaration("Equals",
                new[] {generator.ParameterDeclaration("other", SyntaxFactory.ParseTypeName(className))},
                returnType: SyntaxFactory.ParseTypeName("bool"), accessibility: Accessibility.Public, statements: new[]
                {
                   compareStatement == null ? 
                       generator.ThrowStatement(generator.ObjectCreationExpression(SyntaxFactory.ParseTypeName("NotImplementedException"))): 
                       generator.ReturnStatement(compareStatement)
                });
            return equalsMethod as MethodDeclarationSyntax;
        }

        private static SyntaxNode GetCompareStatement(SyntaxGenerator generator, TypeDeclarationSyntax typeDeclaration, List<SyntaxNode> compareStatements, SyntaxNode compareWithNull)
        {
            if (compareStatements.Count == 0)
            {
                return null;
            }

            return typeDeclaration is ClassDeclarationSyntax
                ? compareStatements.Aggregate(compareWithNull, generator.LogicalAndExpression)
                : compareStatements.Aggregate(generator.LogicalAndExpression);
        }
    }
}
