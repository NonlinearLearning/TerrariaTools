using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class NamespaceBlockPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        BaseNamespaceDeclarationSyntax? namespaceDeclaration = frontendContext.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        string suffix = namespaceDeclaration?.Name.ToString() ?? "<global>";
        diff.AddNode(new NamespaceBlockNode($"namespace-block:{suffix}", suffix, suffix));
    }
}
