using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace DDDToolbox.Utils
{
    public static class DocumentExtensions
    {
        public static async Task<Document> ReplaceNodes(this Document document, SyntaxNode oldNode, SyntaxNode newNode, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(oldNode, newNode);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
