namespace TerrariaTools.Dome.Core.Cpg;

public sealed class NamespaceCreatorPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (NamespaceBlockNode namespaceBlock in Context.Cpg.Nodes.OfType<NamespaceBlockNode>())
        {
            string namespaceName = namespaceBlock.Name ?? "<global>";
            diff.AddNode(new NamespaceNode($"namespace:{namespaceName}", namespaceName, namespaceBlock.FullName));
        }
    }
}
