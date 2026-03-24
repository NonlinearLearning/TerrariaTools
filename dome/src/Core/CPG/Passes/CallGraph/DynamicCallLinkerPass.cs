namespace TerrariaTools.Dome.Core.Cpg;

public sealed class DynamicCallLinkerPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (CallNode call in Context.Cpg.GetNodesByKind<CallNode>(NodeKinds.Call))
        {
            if (string.IsNullOrWhiteSpace(call.OwnerMethodName) ||
                string.IsNullOrWhiteSpace(call.TargetMethodName))
            {
                continue;
            }

            string ownerMethodId = NodeIdFactory.Method(call.ContainingTypeName, call.OwnerMethodName);
            if (Context.Cpg.FindNodeById<StoredNode>(ownerMethodId) is null)
            {
                continue;
            }

            string[] targetMethodIds = ResolveTargetMethodIds(call).ToArray();
            foreach (string targetMethodId in targetMethodIds)
            {
                if (Context.Cpg.FindNodeById<StoredNode>(targetMethodId) is not null)
                {
                    diff.AddEdge(new CpgEdge(EdgeKinds.Call, ownerMethodId, targetMethodId));
                }
            }
        }
    }

    private IEnumerable<string> ResolveTargetMethodIds(CallNode call)
    {
        string[] receiverTargetIds = Context.Cpg.GetOutgoingEdges(EdgeKinds.Receiver, call.Id)
            .Select(edge => edge.TargetId)
            .ToArray();

        HashSet<string> candidateMethodIds = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(call.ResolvedTargetMethodId))
        {
            candidateMethodIds.Add(call.ResolvedTargetMethodId);
        }

        foreach (string receiverTargetId in receiverTargetIds)
        {
            foreach (string declarationNodeId in Context.Cpg.GetOutgoingEdges(EdgeKinds.Ref, receiverTargetId)
                         .Select(edge => edge.TargetId))
            {
                StoredNode? declarationNode = Context.Cpg.FindNodeById<StoredNode>(declarationNodeId);
                string? containingTypeName = declarationNode switch
                {
                    LocalNode local => local.TypeFullName,
                    MethodParameterInNode parameter => parameter.TypeFullName,
                    MemberNode member => member.TypeFullName,
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(containingTypeName))
                {
                    foreach (string typeName in ExpandCandidateTypeNames(containingTypeName))
                    {
                        candidateMethodIds.Add(NodeIdFactory.Method(typeName, call.TargetMethodName));
                    }
                }
            }
        }

        if (candidateMethodIds.Count > 0)
        {
            return candidateMethodIds;
        }

        return [NodeIdFactory.Method(null, call.TargetMethodName)];
    }

    private IEnumerable<string> ExpandCandidateTypeNames(string rootTypeName)
    {
        HashSet<string> candidateTypeNames = new(StringComparer.Ordinal) { rootTypeName };
        Queue<string> pendingTypeNames = new([rootTypeName]);

        while (pendingTypeNames.Count > 0)
        {
            string currentTypeName = pendingTypeNames.Dequeue();
            string currentTypeId = NodeIdFactory.TypeDecl(currentTypeName);
            foreach (CpgEdge inheritsFromEdge in Context.Cpg.GetIncomingEdges(EdgeKinds.InheritsFrom, currentTypeId))
            {
                if (Context.Cpg.FindNodeById<TypeDeclNode>(inheritsFromEdge.SourceId) is not TypeDeclNode derivedTypeDecl ||
                    string.IsNullOrWhiteSpace(derivedTypeDecl.FullName) ||
                    !candidateTypeNames.Add(derivedTypeDecl.FullName))
                {
                    continue;
                }

                pendingTypeNames.Enqueue(derivedTypeDecl.FullName);
            }
        }

        return candidateTypeNames;
    }
}
