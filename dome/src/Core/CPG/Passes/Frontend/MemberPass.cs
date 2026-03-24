using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MemberPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (FieldDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            INamedTypeSymbol? containingTypeSymbol = declaration.Ancestors()
                .OfType<ClassDeclarationSyntax>()
                .Select(classDeclaration => frontendContext.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol)
                .FirstOrDefault(symbol => symbol is not null);
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(containingTypeSymbol)
                ?? declaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(containingTypeName))
            {
                continue;
            }

            ITypeSymbol? symbol = frontendContext.SemanticModel.GetTypeInfo(declaration.Declaration.Type).Type;
            string? typeFullName = RoslynSymbolNameFormatter.GetTypeFullName(symbol, declaration.Declaration.Type.ToString());

            foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
            {
                diff.AddNode(
                    new MemberNode(
                        $"member:{containingTypeName}:{variable.Identifier.ValueText}",
                        variable.Identifier.ValueText,
                        typeFullName,
                        containingTypeName));
            }
        }
    }
}
