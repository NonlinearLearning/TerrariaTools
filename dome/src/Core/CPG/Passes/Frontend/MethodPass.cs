using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MethodPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            string methodName = declaration.Identifier.ValueText;
            IMethodSymbol? symbol = frontendContext.SemanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(symbol?.ContainingType)
                ?? declaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            string? returnTypeName = RoslynSymbolNameFormatter.GetTypeFullName(symbol?.ReturnType, declaration.ReturnType.ToString());
            string? fullName = RoslynSymbolNameFormatter.GetFullName(symbol);
            string? signature = symbol is null
                ? null
                : $"{symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}({string.Join(", ", symbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})";
            diff.AddNode(new MethodNode(NodeIdFactory.Method(containingTypeName, methodName), methodName, containingTypeName, returnTypeName, fullName, signature));
        }
    }
}
