using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// Joern `ReachingDefPass.scala` 的 C# 对应入口。
///
/// 它把 `ReachingDefinitionProblem`、`DataFlowSolver` 和 `DdgGenerator` 串起来，
/// 适合需要直接按 Joern pass 粒度调用的场景。
/// </summary>
public sealed class ReachingDefPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode methodNode in builder.Graph.GetNodes(CpgNodeKind.Method).ToArray())
        {
            ReachingDefinitionProblem problem = ReachingDefinitionProblem.Create(builder.Graph, methodNode);
            DataFlowSolution<CpgNode, IReadOnlySet<DataFlowDefinition>> solution =
                new DataFlowSolver().CalculateForward(problem.Problem);
            new DdgGenerator().Generate(builder, solution);
        }
    }
}
