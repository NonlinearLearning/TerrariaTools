namespace TerrariaTools.Dome.Core.Cpg;

public sealed class FieldAccessLinkerPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (FieldIdentifierNode fieldIdentifier in Context.Cpg.GetNodesByKind<FieldIdentifierNode>(NodeKinds.FieldIdentifier))
        {
            foreach (CpgEdge refEdge in Context.Cpg.GetOutgoingEdges(EdgeKinds.Ref, fieldIdentifier.Id))
            {
                if (Context.Cpg.FindNodeById<MemberNode>(refEdge.TargetId) is not null)
                {
                    diff.AddEdge(new CpgEdge(EdgeKinds.FieldAccess, fieldIdentifier.Id, refEdge.TargetId));
                }
            }
        }
    }
}
