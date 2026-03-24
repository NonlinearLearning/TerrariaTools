namespace TerrariaTools.Dome.Core.Cpg;

public sealed class CdgPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>())
        {
            string[] controlFlowNodeIds = GetControlFlowNodeIds(method).ToArray();
            if (controlFlowNodeIds.Length == 0)
            {
                continue;
            }

            HashSet<string> methodNodeIds = new(controlFlowNodeIds, StringComparer.Ordinal) { method.Id };
            List<CpgEdge> cfgEdges = Context.Cpg.Edges
                .Where(
                    edge =>
                        string.Equals(edge.Label, EdgeKinds.Cfg, StringComparison.Ordinal) &&
                        methodNodeIds.Contains(edge.SourceId) &&
                        methodNodeIds.Contains(edge.TargetId))
                .ToList();
            Dictionary<string, HashSet<string>> postDominators = BuildPostDominatorSets(methodNodeIds, cfgEdges);
            Dictionary<string, string?> immediatePostDominators = BuildImmediatePostDominators(postDominators);
            HashSet<string> controlDependentTargetIds = new(StringComparer.Ordinal);

            foreach (CpgEdge cfgEdge in cfgEdges)
            {
                if (!IsControlDependenceSource(cfgEdge.SourceId))
                {
                    continue;
                }

                if (postDominators[cfgEdge.SourceId].Contains(cfgEdge.TargetId))
                {
                    continue;
                }

                string? runnerId = cfgEdge.TargetId;
                while (!string.IsNullOrWhiteSpace(runnerId) &&
                       !postDominators[cfgEdge.SourceId].Contains(runnerId))
                {
                    diff.AddEdge(new CpgEdge(EdgeKinds.Cdg, cfgEdge.SourceId, runnerId));
                    controlDependentTargetIds.Add(runnerId);
                    immediatePostDominators.TryGetValue(runnerId, out runnerId);
                }
            }

            foreach (string nodeId in controlFlowNodeIds.Where(id => !IsControlStructureId(id)))
            {
                if (!controlDependentTargetIds.Contains(nodeId))
                {
                    diff.AddEdge(new CpgEdge(EdgeKinds.Cdg, method.Id, nodeId));
                }
            }
        }
    }

    private IEnumerable<string> GetControlFlowNodeIds(MethodNode method)
    {
        IEnumerable<(string Id, int Order)> controlStructureNodes = Context.Cpg.Nodes
            .OfType<ControlStructureNode>()
            .Where(
                controlStructure =>
                    string.Equals(controlStructure.MethodName, method.Name, StringComparison.Ordinal) &&
                    string.Equals(controlStructure.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
            .Select(controlStructure => (controlStructure.Id, controlStructure.Order));

        IEnumerable<(string Id, int Order)> callNodes = Context.Cpg.Nodes
            .OfType<CallNode>()
            .Where(
                call =>
                    string.Equals(call.OwnerMethodName, method.Name, StringComparison.Ordinal) &&
                    string.Equals(call.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
            .Select(call => (call.Id, call.Order ?? int.MaxValue));

        IEnumerable<(string Id, int Order)> returnNodes = Context.Cpg.Nodes
            .OfType<ReturnNode>()
            .Where(
                returnNode =>
                    string.Equals(returnNode.MethodName, method.Name, StringComparison.Ordinal) &&
                    string.Equals(returnNode.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
            .Select(returnNode => (returnNode.Id, returnNode.Order));

        return controlStructureNodes
            .Concat(callNodes)
            .Concat(returnNodes)
            .OrderBy(node => node.Order)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Select(node => node.Id);
    }

    private static Dictionary<string, HashSet<string>> BuildPostDominatorSets(
        IReadOnlyCollection<string> nodeIds,
        IReadOnlyCollection<CpgEdge> cfgEdges)
    {
        HashSet<string> allNodeIds = new(nodeIds, StringComparer.Ordinal);
        HashSet<string> exitNodeIds = nodeIds
            .Where(nodeId => !cfgEdges.Any(edge => string.Equals(edge.SourceId, nodeId, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, HashSet<string>> postDominators = new(StringComparer.Ordinal);
        foreach (string nodeId in nodeIds)
        {
            postDominators[nodeId] = exitNodeIds.Contains(nodeId)
                ? new HashSet<string>([nodeId], StringComparer.Ordinal)
                : new HashSet<string>(allNodeIds, StringComparer.Ordinal);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (string nodeId in nodeIds.Where(id => !exitNodeIds.Contains(id)))
            {
                List<string> successorIds = cfgEdges
                    .Where(edge => string.Equals(edge.SourceId, nodeId, StringComparison.Ordinal))
                    .Select(edge => edge.TargetId)
                    .ToList();
                if (successorIds.Count == 0)
                {
                    continue;
                }

                HashSet<string> nextPostDominators = new(postDominators[successorIds[0]], StringComparer.Ordinal);
                foreach (string successorId in successorIds.Skip(1))
                {
                    nextPostDominators.IntersectWith(postDominators[successorId]);
                }

                nextPostDominators.Add(nodeId);
                if (!nextPostDominators.SetEquals(postDominators[nodeId]))
                {
                    postDominators[nodeId] = nextPostDominators;
                    changed = true;
                }
            }
        }
        while (changed);

        return postDominators;
    }

    private static Dictionary<string, string?> BuildImmediatePostDominators(
        IReadOnlyDictionary<string, HashSet<string>> postDominators)
    {
        Dictionary<string, string?> immediatePostDominators = new(StringComparer.Ordinal);
        foreach ((string nodeId, HashSet<string> postDominatorSet) in postDominators)
        {
            string[] strictPostDominators = postDominatorSet
                .Where(candidate => !string.Equals(candidate, nodeId, StringComparison.Ordinal))
                .ToArray();

            string? immediatePostDominatorId = strictPostDominators
                .FirstOrDefault(
                    candidate =>
                        strictPostDominators
                            .Where(other => !string.Equals(other, candidate, StringComparison.Ordinal))
                            .All(other => !postDominators[other].Contains(candidate)));

            immediatePostDominators[nodeId] = immediatePostDominatorId;
        }

        return immediatePostDominators;
    }

    private static bool IsControlDependenceSource(string nodeId)
    {
        return IsControlStructureId(nodeId);
    }

    private static bool IsControlStructureId(string nodeId)
    {
        return nodeId.StartsWith("control-structure:", StringComparison.Ordinal);
    }
}
