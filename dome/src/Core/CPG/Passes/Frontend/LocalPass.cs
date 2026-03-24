using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class LocalPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodDeclarationSyntax method in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            string methodName = method.Identifier.ValueText;
            IMethodSymbol? methodSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(methodSymbol?.ContainingType)
                ?? method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            int localIndex = 0;

            foreach (LocalDeclarationStatementSyntax declaration in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                ITypeSymbol? symbol = frontendContext.SemanticModel.GetTypeInfo(declaration.Declaration.Type).Type;
                string? typeFullName = RoslynSymbolNameFormatter.GetTypeFullName(symbol, declaration.Declaration.Type.ToString());

                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    diff.AddNode(
                        new LocalNode(
                            NodeIdFactory.Local(containingTypeName, methodName, variable.Identifier.ValueText, localIndex),
                            methodName,
                            variable.Identifier.ValueText,
                            localIndex,
                            typeFullName,
                            containingTypeName));
                    localIndex++;
                }
            }
        }
    }
}
