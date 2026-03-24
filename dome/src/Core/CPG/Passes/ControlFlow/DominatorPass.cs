namespace TerrariaTools.Dome.Core.Cpg;

public sealed class DominatorPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>())
        {
            List<string> controlFlowNodeIds = [method.Id];
            controlFlowNodeIds.AddRange(
                GetControlFlowNodeIds(method));

            if (controlFlowNodeIds.Count <= 1)
            {
                continue;
            }

            List<CpgEdge> cfgEdges = Context.Cpg.Edges
                .Where(
                    edge =>
                        string.Equals(edge.Label, "CFG", StringComparison.Ordinal) &&
                        controlFlowNodeIds.Contains(edge.SourceId, StringComparer.Ordinal) &&
                        controlFlowNodeIds.Contains(edge.TargetId, StringComparer.Ordinal))
                .ToList();

            HashSet<string> allNodeIds = controlFlowNodeIds.ToHashSet(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> dominators = new(StringComparer.Ordinal);
            dominators[method.Id] = [method.Id];

            foreach (string nodeId in controlFlowNodeIds.Where(id => !string.Equals(id, method.Id, StringComparison.Ordinal)))
            {
                dominators[nodeId] = new HashSet<string>(allNodeIds, StringComparer.Ordinal);
            }

            bool changed;
            do
            {
                changed = false;
                foreach (string nodeId in controlFlowNodeIds.Where(id => !string.Equals(id, method.Id, StringComparison.Ordinal)))
                {
                    List<string> predecessors = cfgEdges
                        .Where(edge => string.Equals(edge.TargetId, nodeId, StringComparison.Ordinal))
                        .Select(edge => edge.SourceId)
                        .ToList();
                    if (predecessors.Count == 0)
                    {
                        continue;
                    }

                    HashSet<string> nextDominators = new(dominators[predecessors[0]], StringComparer.Ordinal);
                    foreach (string predecessor in predecessors.Skip(1))
                    {
                        nextDominators.IntersectWith(dominators[predecessor]);
                    }

                    nextDominators.Add(nodeId);
                    if (!nextDominators.SetEquals(dominators[nodeId]))
                    {
                        dominators[nodeId] = nextDominators;
                        changed = true;
                    }
                }
            }
            while (changed);

            foreach ((string nodeId, HashSet<string> dominatedBy) in dominators)
            {
                foreach (string dominatorId in dominatedBy.Where(id => !string.Equals(id, nodeId, StringComparison.Ordinal)))
                {
                    diff.AddEdge(new CpgEdge("DOMINATE", dominatorId, nodeId));
                }
            }

            Dictionary<string, HashSet<string>> postDominators = ComputePostDominators(controlFlowNodeIds, cfgEdges);
            foreach ((string nodeId, HashSet<string> postDominatedBy) in postDominators)
            {
                foreach (string postDominatorId in postDominatedBy.Where(id => !string.Equals(id, nodeId, StringComparison.Ordinal)))
                {
                    diff.AddEdge(new CpgEdge("POST_DOMINATE", postDominatorId, nodeId));
                }
            }
        }
    }

    private static Dictionary<string, HashSet<string>> ComputePostDominators(
        IReadOnlyList<string> controlFlowNodeIds,
        IReadOnlyList<CpgEdge> cfgEdges)
    {
        HashSet<string> allNodeIds = controlFlowNodeIds.ToHashSet(StringComparer.Ordinal);
        HashSet<string> exitNodeIds = controlFlowNodeIds
            .Where(nodeId => !cfgEdges.Any(edge => string.Equals(edge.SourceId, nodeId, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, HashSet<string>> postDominators = new(StringComparer.Ordinal);
        foreach (string nodeId in controlFlowNodeIds)
        {
            postDominators[nodeId] = exitNodeIds.Contains(nodeId)
                ? new HashSet<string>([nodeId], StringComparer.Ordinal)
                : new HashSet<string>(allNodeIds, StringComparer.Ordinal);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (string nodeId in controlFlowNodeIds.Where(id => !exitNodeIds.Contains(id)))
            {
                List<string> successors = cfgEdges
                    .Where(edge => string.Equals(edge.SourceId, nodeId, StringComparison.Ordinal))
                    .Select(edge => edge.TargetId)
                    .ToList();
                if (successors.Count == 0)
                {
                    continue;
                }

                HashSet<string> nextPostDominators = new(postDominators[successors[0]], StringComparer.Ordinal);
                foreach (string successor in successors.Skip(1))
                {
                    nextPostDominators.IntersectWith(postDominators[successor]);
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
}
