namespace NLISSN.Rules;

public sealed class RuleGraphCompiler
{
    public CompiledRuleGraph Compile(IReadOnlyList<RuleGraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var byId = new Dictionary<RuleNodeId, RuleGraphNode>();
        foreach (var node in nodes)
        {
            if (!byId.TryAdd(node.NodeId, node))
            {
                throw new InvalidOperationException($"Rule graph contains duplicate node '{node.NodeId.Value}'.");
            }
        }

        var declarationIndexes = nodes
          .Select((node, index) => (node.NodeId, index))
          .ToDictionary(entry => entry.NodeId, entry => entry.index);
        var indegrees = nodes.ToDictionary(node => node.NodeId, _ => 0);
        var downstream = nodes.ToDictionary(
          node => node.NodeId,
          _ => new List<RuleNodeId>());

        foreach (var node in nodes)
        {
            var distinctDependencies = new HashSet<RuleNodeId>();
            foreach (var dependency in node.Dependencies)
            {
                if (!byId.TryGetValue(dependency.Producer, out var producer))
                {
                    throw new InvalidOperationException(
                      $"Rule node '{node.NodeId.Value}' depends on unknown producer '{dependency.Producer.Value}'.");
                }

                if (!producer.ProducedOutputs.Contains(dependency.RequiredOutput))
                {
                    throw new InvalidOperationException(
                      $"Rule node '{node.NodeId.Value}' requires output '{dependency.RequiredOutput}' from producer '{dependency.Producer.Value}', but it is not produced.");
                }

                if (!distinctDependencies.Add(dependency.Producer))
                {
                    throw new InvalidOperationException(
                      $"Rule node '{node.NodeId.Value}' declares producer '{dependency.Producer.Value}' more than once.");
                }

                indegrees[node.NodeId]++;
                downstream[dependency.Producer].Add(node.NodeId);
            }
        }

        var ready = new PriorityQueue<RuleNodeId, int>();
        foreach (var node in nodes)
        {
            if (indegrees[node.NodeId] == 0)
            {
                ready.Enqueue(node.NodeId, declarationIndexes[node.NodeId]);
            }
        }

        var ordered = new List<RuleGraphNode>(nodes.Count);
        while (ready.TryDequeue(out var nodeId, out _))
        {
            ordered.Add(byId[nodeId]);
            foreach (var child in downstream[nodeId].OrderBy(id => declarationIndexes[id]))
            {
                indegrees[child]--;
                if (indegrees[child] == 0)
                {
                    ready.Enqueue(child, declarationIndexes[child]);
                }
            }
        }

        if (ordered.Count != nodes.Count)
        {
            var cycleNodeIds = nodes
              .Where(node => indegrees[node.NodeId] > 0)
              .Select(node => node.NodeId.Value);
            throw new InvalidOperationException($"Rule graph contains a cycle: {string.Join(", ", cycleNodeIds)}.");
        }

        var normalizedNodes = ordered
          .Select(node => node with
          {
              Dependencies = node.Dependencies
                .OrderBy(dependency => declarationIndexes[dependency.Producer])
                .ToList()
          })
          .ToList();
        return new CompiledRuleGraph(
          normalizedNodes,
          normalizedNodes
            .Select((node, index) => (node.NodeId, index))
            .ToDictionary(entry => entry.NodeId, entry => entry.index),
          downstream.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<RuleNodeId>)entry.Value
              .OrderBy(id => declarationIndexes[id])
              .ToList()));
    }
}
