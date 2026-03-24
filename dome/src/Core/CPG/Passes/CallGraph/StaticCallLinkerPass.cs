namespace TerrariaTools.Dome.Core.Cpg;

public sealed class StaticCallLinkerPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (CallNode call in Context.Cpg.Nodes.OfType<CallNode>())
        {
            if (string.IsNullOrWhiteSpace(call.OwnerMethodName) ||
                string.IsNullOrWhiteSpace(call.ResolvedTargetMethodId))
            {
                continue;
            }

            string ownerMethodId = NodeIdFactory.Method(call.ContainingTypeName, call.OwnerMethodName);
            if (Context.Cpg.FindNodeById<StoredNode>(ownerMethodId) is null ||
                Context.Cpg.FindNodeById<StoredNode>(call.ResolvedTargetMethodId) is null)
            {
                continue;
            }

            diff.AddEdge(new CpgEdge(EdgeKinds.Call, ownerMethodId, call.ResolvedTargetMethodId));
        }
    }
}
