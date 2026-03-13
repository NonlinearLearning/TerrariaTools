namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 函数删除影响分析器。
/// </summary>
public sealed class FunctionImpactAnalyzer
{
    /// <summary>
    /// 分析函数删除对其他函数的影响。
    /// </summary>
    /// <param name="plan">审计计划。</param>
    /// <param name="services">分析服务。</param>
    /// <param name="request">函数图请求。</param>
    /// <returns>受影响的函数集合。</returns>
    public FunctionImpactSet Analyze(
        AuditPlan plan,
        AnalysisServices services,
        FunctionGraphRequest request)
    {
        return Analyze(plan, services.FunctionGraphs.GetSnapshot(request));
    }

    /// <summary>
    /// 仅基于 Calls 边计算函数删除的一跳影响范围。
    /// </summary>
    public FunctionImpactSet Analyze(AuditPlan plan, FunctionGraphSnapshot snapshot)
    {
        var deletedFunctionIds = plan.Changes
            .Where(change => change.Target.TargetKind == TargetKind.Method && change.Action.Kind == PlanActionKind.Delete)
            .Select(change => change.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (deletedFunctionIds.Length == 0)
        {
            return new FunctionImpactSet(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                1,
                new[] { FunctionDependencyKind.Calls });
        }

        var deletedFunctionSet = deletedFunctionIds.ToHashSet(StringComparer.Ordinal);
        var affectedFunctionIds = new HashSet<string>(StringComparer.Ordinal);

        var functionGraph = snapshot.Graph;
        foreach (var edge in functionGraph.Edges.Where(edge => edge.Kind == FunctionDependencyKind.Calls))
        {
            if (deletedFunctionSet.Contains(edge.TargetMemberId.Value) &&
                !deletedFunctionSet.Contains(edge.SourceMemberId.Value))
            {
                affectedFunctionIds.Add(edge.SourceMemberId.Value);
            }

            if (deletedFunctionSet.Contains(edge.SourceMemberId.Value) &&
                !deletedFunctionSet.Contains(edge.TargetMemberId.Value))
            {
                affectedFunctionIds.Add(edge.TargetMemberId.Value);
            }
        }

        var affectedDocumentPaths = functionGraph.Nodes
            .Where(node => affectedFunctionIds.Contains(node.MemberId.Value))
            .Select(node => node.DocumentPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new FunctionImpactSet(
            deletedFunctionIds,
            affectedFunctionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            affectedDocumentPaths,
            1,
            new[] { FunctionDependencyKind.Calls });
    }
}
