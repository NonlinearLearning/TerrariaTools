namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MethodRefLinkerPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodRefNode methodRef in Context.Cpg.GetNodesByKind<MethodRefNode>(NodeKinds.MethodRef))
        {
            string[] targetMethodIds = Context.Cpg.GetOutgoingEdges(EdgeKinds.Ref, methodRef.Id)
                .Select(edge => edge.TargetId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (targetMethodIds.Length == 0)
            {
                continue;
            }

            string[] ownerMethodIds = Context.Cpg.GetIncomingEdges(EdgeKinds.Argument, methodRef.Id)
                .Select(edge => edge.SourceId)
                .Select(callNodeId => Context.Cpg.FindNodeById<CallNode>(callNodeId))
                .Where(call => call is not null && !string.IsNullOrWhiteSpace(call.OwnerMethodName))
                .Select(call => NodeIdFactory.Method(call!.ContainingTypeName, call.OwnerMethodName))
                .Where(ownerMethodId => Context.Cpg.FindNodeById<StoredNode>(ownerMethodId) is not null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (string ownerMethodId in ownerMethodIds)
            {
                foreach (string targetMethodId in targetMethodIds)
                {
                    if (Context.Cpg.FindNodeById<StoredNode>(targetMethodId) is not null)
                    {
                        diff.AddEdge(new CpgEdge(EdgeKinds.Call, ownerMethodId, targetMethodId));
                    }
                }
            }
        }
    }
}
