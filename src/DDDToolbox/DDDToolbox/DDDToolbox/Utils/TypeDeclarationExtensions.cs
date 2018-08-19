using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolbox.Utils
{
    public static class TypeDeclarationExtensions{

        public static TypeDeclarationSyntax WithMembers(this TypeDeclarationSyntax td, IEnumerable<MemberDeclarationSyntax> newMembers)
        {
            switch (td)
            {
                case ClassDeclarationSyntax cd:
                    return cd.WithMembers(new SyntaxList<MemberDeclarationSyntax>(newMembers));
                case StructDeclarationSyntax sd:
                    return sd.WithMembers(new SyntaxList<MemberDeclarationSyntax>(newMembers));
                default:
                    return td;
            }
        }
    }
}