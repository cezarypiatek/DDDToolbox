using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using DDDToolbox.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace DDDToolbox
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(ConvertToRecordTypeRefactoring)), Shared]
    internal class ConvertToRecordTypeRefactoring : CodeRefactoringProvider
    {
        public const string Title = "Convert to record type";


        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            if (node is ClassDeclarationSyntax || node is StructDeclarationSyntax)
            {
                context.RegisterRefactoring(CodeAction.Create(title: Title, createChangedDocument: c => ConvertToRecord(context.Document, node as TypeDeclarationSyntax, c), equivalenceKey: Title));
            }
        }

        private async Task<Document> ConvertToRecord(Document document, TypeDeclarationSyntax classDeclaration, CancellationToken cancellationToken)
        {
            var generator = SyntaxGenerator.GetGenerator(document);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var n1 = MakeClassReadonlyRefactoring.ConvertToReadonly(classDeclaration, generator);
            var n2 = AddStructuralEqualityRefactoring.ExtendsWithStructuralEquality(n1, generator, semanticModel, classDeclaration);
            return await document.ReplaceNodes(classDeclaration, n2, cancellationToken);
        }
    }
}
