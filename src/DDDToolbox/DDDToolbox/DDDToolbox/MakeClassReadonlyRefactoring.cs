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
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(MakeClassReadonlyRefactoring)), Shared]
    internal class MakeClassReadonlyRefactoring : CodeRefactoringProvider
    {
        public const string Title = "Make class readonly";

        public string Sample { get; set; }
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            if (node is ClassDeclarationSyntax || node is StructDeclarationSyntax)
            {
                context.RegisterRefactoring(CodeAction.Create(title: Title, createChangedDocument: c => MakeClassReadonly(context.Document, node as TypeDeclarationSyntax, c), equivalenceKey: Title));
            }
        }

        private async Task<Document> MakeClassReadonly(Document document, TypeDeclarationSyntax typeDeclaration, CancellationToken cancellationToken)
        {
            var generator = SyntaxGenerator.GetGenerator(document);
            var newClassDeclaration = ConvertToReadonly(typeDeclaration, generator);
            return await document.ReplaceNodes(typeDeclaration, newClassDeclaration, cancellationToken);
        }

        public static TypeDeclarationSyntax ConvertToReadonly(TypeDeclarationSyntax typeDeclaration, SyntaxGenerator generator)
        {
            var propertiesToSetFrmConstructor = new List<PropertyDeclarationSyntax>();

            var newMembers = typeDeclaration.Members.Select(x =>
            {
                if (x is PropertyDeclarationSyntax propertyDeclaration && propertyDeclaration.Modifiers.Any(SyntaxKind.PublicKeyword) && propertyDeclaration.AccessorList!=null)
                {
                    propertiesToSetFrmConstructor.Add(propertyDeclaration);
                    var accessorsWithoutSetter = propertyDeclaration.AccessorList.Accessors.Where(a => a.Kind() != SyntaxKind.SetAccessorDeclaration);
                    return propertyDeclaration.WithAccessorList(SyntaxFactory.AccessorList(new SyntaxList<AccessorDeclarationSyntax>(accessorsWithoutSetter)));
                }
                return x;
            }).ToList();

            var newConstructor = GenerateConstructor(typeDeclaration, generator, propertiesToSetFrmConstructor) as ConstructorDeclarationSyntax;
            var newConstructorParameters = GetConstructorParameters(newConstructor);
            var allConstructors = typeDeclaration.Members.Where(x => x.IsKind(SyntaxKind.ConstructorDeclaration)).OfType<ConstructorDeclarationSyntax>();
            var newConstructorAlreadyExists =  allConstructors.Any(c => GetConstructorParameters(c).SetEquals(newConstructorParameters));
            if (newConstructorAlreadyExists == false)
            {
                newMembers.Add(newConstructor);
            }
            
            return typeDeclaration.WithMembers(newMembers);
        }

        private static HashSet<(string, string)> GetConstructorParameters(ConstructorDeclarationSyntax newConstructor)
        {
            var newConstructorParameters = new HashSet<(string, string)>();
            foreach (var parameter in newConstructor.ParameterList.Parameters)
            {
                newConstructorParameters.Add((parameter.Type.ToFullString(), parameter.Identifier.Text));
            }

            return newConstructorParameters;
        }

        private static MemberDeclarationSyntax GenerateConstructor(TypeDeclarationSyntax typeDeclaration, SyntaxGenerator generator, List<PropertyDeclarationSyntax> propertiesToSetFrmConstructor)
        {
            var constructorParameters = propertiesToSetFrmConstructor.Select(x => generator.ParameterDeclaration(ToParameterName(x.Identifier.Text), x.Type));
            var thisIdentifier = generator.ThisExpression();
            var assignments = propertiesToSetFrmConstructor.Select(x =>
            {
                var memberAccess = generator.MemberAccessExpression(thisIdentifier, x.Identifier.Text);
                var parameterAccess = generator.IdentifierName(ToParameterName(x.Identifier.Text));
                return generator.AssignmentStatement(memberAccess, parameterAccess);
            });
            return generator.ConstructorDeclaration(typeDeclaration.Identifier.Text, parameters: constructorParameters, accessibility: Accessibility.Public, statements: assignments) as MemberDeclarationSyntax;
        }

        private static string ToParameterName(string name)
        {
            return $"{name.Substring(0, 1).ToLower()}{name.Substring(1)}";
        }
    }
}
