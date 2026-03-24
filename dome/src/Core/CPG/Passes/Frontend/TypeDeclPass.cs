using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeDeclPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (ClassDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            string typeName = declaration.Identifier.ValueText;
            BaseTypeSyntax? baseTypeSyntax = declaration.BaseList?.Types.FirstOrDefault();
            INamedTypeSymbol? symbol = frontendContext.SemanticModel.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
            ITypeSymbol? baseTypeSymbol = baseTypeSyntax is null
                ? null
                : frontendContext.SemanticModel.GetTypeInfo(baseTypeSyntax.Type).Type;
            string? baseTypeName = RoslynSymbolNameFormatter.GetTypeFullName(baseTypeSymbol, baseTypeSyntax?.Type.ToString());
            string? fullName = RoslynSymbolNameFormatter.GetFullName(symbol) ?? typeName;
            diff.AddNode(new TypeDeclNode(NodeIdFactory.TypeDecl(fullName), typeName, baseTypeName, fullName));
        }
    }
}
