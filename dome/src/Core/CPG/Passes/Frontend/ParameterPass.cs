using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class ParameterPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            string methodName = declaration.Identifier.ValueText;
            IMethodSymbol? methodSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(methodSymbol?.ContainingType)
                ?? declaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

            for (int index = 0; index < declaration.ParameterList.Parameters.Count; index++)
            {
                ParameterSyntax parameter = declaration.ParameterList.Parameters[index];
                IParameterSymbol? symbol = frontendContext.SemanticModel.GetDeclaredSymbol(parameter) as IParameterSymbol;
                string? typeFullName = RoslynSymbolNameFormatter.GetTypeFullName(symbol?.Type, parameter.Type?.ToString());
                diff.AddNode(
                    new MethodParameterInNode(
                        NodeIdFactory.MethodParameterIn(containingTypeName, methodName, parameter.Identifier.ValueText, index + 1),
                        methodName,
                        parameter.Identifier.ValueText,
                        index + 1,
                        typeFullName,
                        containingTypeName));
            }
        }
    }
}
