namespace TerrariaTools.Dome.Core.Cpg;

public static class DiffGraphApplier
{
    public static void Apply(DomeCpg cpg, DiffGraph diff)
    {
        foreach (StoredNode node in diff.Nodes)
        {
            cpg.AddNode(node);
        }

        foreach (CpgEdge edge in diff.Edges)
        {
            cpg.AddEdge(edge);
        }
    }
}
