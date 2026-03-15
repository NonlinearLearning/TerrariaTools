namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

/// <summary>
/// 兼容上下文工厂。
/// </summary>
public sealed class CompatibilityExecutionContextFactory
{
    /// <summary>
    /// 基于分析视图创建规则执行所需的分析上下文。
    /// </summary>
    /// <param name="analysisView">分析结果视图。</param>
    /// <returns>分析上下文。</returns>
    public AnalysisContext CreateContext(AnalysisResultModel analysisView)
    {
        var statementFacts = new StatementFactsIndex(
            analysisView.Targets
                .Where(target => target.Target.TargetKind == TargetKind.Statement)
                .GroupBy(target => target.Target.MemberId.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<StatementFact>)group
                        .OrderBy(target => target.Target.SpanStart)
                        .ThenBy(target => target.Target.TargetKey, StringComparer.Ordinal)
                        .Select(target => new StatementFact(
                            target.Target.TargetKey,
                            target.Target.MemberId,
                            target.StatementKind,
                            target.DefinesSymbols,
                            target.UsesSymbols,
                            target.InvokedMemberIds,
                            target.ScopeMode,
                            target.ScopeId,
                            target.ParentScopeId,
                            target.Target.SpanStart,
                            target.Target.SpanLength))
                        .ToArray(),
                    StringComparer.Ordinal));
        var snapshot = new AnalysisExecutionSnapshot(
            analysisView,
            FunctionIndex.Empty,
            FunctionFactsIndex.Empty,
            statementFacts);
        var services = new AnalysisServices(
            new NoOpInheritanceQueryService(),
            new NoOpReferenceQueryService(),
            new StatementAnalysisService(statementFacts),
            new NoOpFunctionGraphProvider());
        return AnalysisContext.Create(snapshot, services);
    }

    /// <summary>
    /// 创建规则执行上下文。
    /// </summary>
    /// <param name="reason">执行原因。</param>
    /// <returns>规则执行上下文。</returns>
    public RuleExecutionContext CreateExecutionContext(string reason) =>
        new(
            "MarkingRuleEngine",
            null,
            StatementScopeMode.MinimalBlock,
            CancellationToken.None,
            reason);
}

/// <summary>
/// 空实现的继承查询服务。
/// </summary>
internal sealed class NoOpInheritanceQueryService : IInheritanceQueryService
{
    /// <inheritdoc />
    public bool IsOverrideMember(string memberId) => false;

    /// <inheritdoc />
    public bool ImplementsInterfaceMember(string memberId) => false;

    /// <inheritdoc />
    public bool IsInInheritanceChain(string typeId) => false;
}

/// <summary>
/// 空实现的引用查询服务。
/// </summary>
internal sealed class NoOpReferenceQueryService : IReferenceQueryService
{
    /// <inheritdoc />
    public bool HasReferences(string symbolOrMemberId) => false;

    /// <inheritdoc />
    public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) => Array.Empty<MemberId>();

    /// <inheritdoc />
    public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => Array.Empty<string>();
}

/// <summary>
/// 空实现的函数图提供器。
/// </summary>
internal sealed class NoOpFunctionGraphProvider : IFunctionGraphProvider
{
    private static readonly FunctionGraphSnapshot EmptySnapshot = new(
        FunctionGraphScope.ExpandedMembers,
        Array.Empty<MemberId>(),
        Array.Empty<string>(),
        new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()));

    /// <inheritdoc />
    public FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request) => EmptySnapshot;
}
