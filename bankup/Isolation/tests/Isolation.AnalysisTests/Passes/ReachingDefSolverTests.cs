using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes.DataFlow;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class ReachingDefSolverTests
{
    [Fact]
    public void DataFlowSolver_forwardMergesPredecessorsUntilFixedPoint()
    {
        IntFlowGraph flowGraph = new(
            reversePostOrder: new[] { 1, 2, 3, 4 },
            successors: new Dictionary<int, int[]> { [1] = new[] { 2, 3 }, [2] = new[] { 4 }, [3] = new[] { 4 }, [4] = Array.Empty<int>() });
        SetTransferFunction transfer = new(new Dictionary<int, string[]> { [1] = new[] { "entry" }, [2] = new[] { "left" }, [3] = new[] { "right" } });
        DataFlowProblem<int, IReadOnlySet<string>> problem = DataFlowProblem<int, IReadOnlySet<string>>.Forward(
            flowGraph,
            transfer,
            (left, right) => left.Concat(right).ToHashSet(StringComparer.Ordinal),
            new EmptySetInit(flowGraph.AllNodesReversePostOrder),
            new HashSet<string>(StringComparer.Ordinal));

        DataFlowSolution<int, IReadOnlySet<string>> solution = new DataFlowSolver().CalculateForward(problem);

        Assert.Equal(new[] { "entry", "left", "right" }, solution.In[4].OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void ReachingDefinitionProblem_createIncludesParametersAndAssignments()
    {
        CpgGraph graph = new();
        CpgNode method = graph.CreateNode(CpgNodeKind.Method);
        CpgNode parameter = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        parameter.SetProperty("Name", "input");
        parameter.SetProperty("AstParentId", method.Id);
        CpgNode local = graph.CreateNode(CpgNodeKind.Local);
        local.SetProperty("Name", "value");
        _ = graph.AddEdge(method.Id, parameter.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(method.Id, local.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(method.Id, local.Id, CpgEdgeKind.Cfg);

        ReachingDefinitionProblem problem = ReachingDefinitionProblem.Create(graph, method);

        Assert.Contains(problem.FlowGraph.AllNodesReversePostOrder, node => node.Id == parameter.Id);
        Assert.Contains(problem.GeneratedDefinitions[parameter], definition => definition.Label == "input");
        Assert.Contains(problem.GeneratedDefinitions[local], definition => definition.Label == "value");
    }

    private sealed class IntFlowGraph : IFlowGraph<int>
    {
        private readonly Dictionary<int, int[]> successors;
        private readonly Dictionary<int, int[]> predecessors;

        public IntFlowGraph(IReadOnlyList<int> reversePostOrder, Dictionary<int, int[]> successors)
        {
            AllNodesReversePostOrder = reversePostOrder;
            AllNodesPostOrder = reversePostOrder.Reverse().ToArray();
            this.successors = successors;
            predecessors = reversePostOrder.ToDictionary(node => node, _ => Array.Empty<int>());
            foreach ((int source, int[] targets) in successors)
            {
                foreach (int target in targets)
                {
                    predecessors[target] = predecessors[target].Append(source).ToArray();
                }
            }
        }

        public IReadOnlyList<int> AllNodesReversePostOrder { get; }

        public IReadOnlyList<int> AllNodesPostOrder { get; }

        public IEnumerable<int> Successors(int node) => successors.TryGetValue(node, out int[]? result) ? result : Array.Empty<int>();

        public IEnumerable<int> Predecessors(int node) => predecessors.TryGetValue(node, out int[]? result) ? result : Array.Empty<int>();
    }

    private sealed class SetTransferFunction : ITransferFunction<int, IReadOnlySet<string>>
    {
        private readonly Dictionary<int, string[]> generated;

        public SetTransferFunction(Dictionary<int, string[]> generated)
        {
            this.generated = generated;
        }

        public IReadOnlySet<string> Apply(int node, IReadOnlySet<string> value)
        {
            return value.Concat(generated.TryGetValue(node, out string[]? items) ? items : Array.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    private sealed class EmptySetInit : IInOutInit<int, IReadOnlySet<string>>
    {
        public EmptySetInit(IEnumerable<int> nodes)
        {
            InitIn = nodes.ToDictionary(node => node, _ => (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal));
            InitOut = nodes.ToDictionary(node => node, _ => (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal));
        }

        public IReadOnlyDictionary<int, IReadOnlySet<string>> InitIn { get; }

        public IReadOnlyDictionary<int, IReadOnlySet<string>> InitOut { get; }
    }
}
