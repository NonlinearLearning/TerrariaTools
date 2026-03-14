namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

public sealed class RuleCompatibilityAdapter
{
    public AnalysisContext CreateContext(AnalysisView analysisView)
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
        var snapshot = new AnalysisSnapshot(
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

    public RuleExecutionContext CreateExecutionContext(string reason) =>
        new(
            "MarkingRuleEngine",
            null,
            StatementScopeMode.MinimalBlock,
            CancellationToken.None,
            reason);
}

internal sealed class NoOpInheritanceQueryService : IInheritanceQueryService
{
    public bool IsOverrideMember(string memberId) => false;

    public bool ImplementsInterfaceMember(string memberId) => false;

    public bool IsInInheritanceChain(string typeId) => false;
}

internal sealed class NoOpReferenceQueryService : IReferenceQueryService
{
    public bool HasReferences(string symbolOrMemberId) => false;

    public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) => Array.Empty<MemberId>();

    public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => Array.Empty<string>();
}

internal sealed class NoOpFunctionGraphProvider : IFunctionGraphProvider
{
    private static readonly FunctionGraphSnapshot EmptySnapshot = new(
        FunctionGraphScope.ExpandedMembers,
        Array.Empty<MemberId>(),
        Array.Empty<string>(),
        new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()));

    public FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request) => EmptySnapshot;
}
