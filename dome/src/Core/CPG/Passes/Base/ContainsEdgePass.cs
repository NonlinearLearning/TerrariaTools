namespace TerrariaTools.Dome.Core.Cpg;

public sealed class ContainsEdgePass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        FileNode? fileNode = Context.Cpg.Nodes.OfType<FileNode>().FirstOrDefault();
        NamespaceBlockNode? namespaceBlock = Context.Cpg.Nodes.OfType<NamespaceBlockNode>().FirstOrDefault();
        if (fileNode is not null && namespaceBlock is not null)
        {
            diff.AddEdge(new CpgEdge("CONTAINS", fileNode.Id, namespaceBlock.Id));
        }

        foreach (CpgEdge astEdge in Context.Cpg.Edges.Where(edge => string.Equals(edge.Label, "AST", StringComparison.Ordinal)))
        {
            StoredNode? sourceNode = Context.Cpg.FindNodeById<StoredNode>(astEdge.SourceId);
            if (sourceNode is NamespaceBlockNode or TypeDeclNode or MethodNode or BlockNode)
            {
                diff.AddEdge(new CpgEdge("CONTAINS", astEdge.SourceId, astEdge.TargetId));
            }
        }
    }
}
