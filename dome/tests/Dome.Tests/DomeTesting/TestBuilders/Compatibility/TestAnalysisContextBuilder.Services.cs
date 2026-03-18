using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

/// <summary>
/// Compatibility-only service stubs for native analysis contexts.
/// </summary>
internal sealed partial class LegacyAnalysisContextBuilder
{
    private ModelAnalysis.AnalysisServices BuildServices()
    {
        return new ModelAnalysis.AnalysisServices(
            _inheritance ?? new StubInheritanceQueryService(),
            _references ?? new StubReferenceQueryService(),
            _statements ?? new StubStatementAnalysisService(_statementSnapshots),
            _functionGraphs ?? new StubFunctionGraphProvider(),
            _symbolDependencies ?? new StubSymbolDependencyGraphProvider(),
            _methodCalls ?? new StubMethodCallQueryService(),
            _dataFlow ?? new StubDataFlowSummaryService(),
            _switchFlows ?? new StubSwitchFlowSummaryService(),
            _callChains ?? new StubCallChainAnalysisService(),
            _advancedAnalysis ?? new StubAdvancedAnalysisSummaryService(),
            _memberCleanup ?? new StubMemberCleanupQueryService());
    }

    private sealed class StubInheritanceQueryService : ModelAnalysis.IInheritanceQueryService
    {
        public bool ImplementsInterfaceMember(string memberId) => false;

        public bool IsInInheritanceChain(string typeId) => false;

        public bool IsOverrideMember(string memberId) => false;
    }

    private sealed class StubReferenceQueryService : ModelAnalysis.IReferenceQueryService
    {
        public IReadOnlyList<ModelPrimitives.MemberId> GetReferencingFunctions(string symbolOrMemberId) => [];

        public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => [];

        public bool HasReferences(string symbolOrMemberId) => false;
    }

    private sealed class StubStatementAnalysisService(
        IReadOnlyDictionary<(string TargetKey, ModelPrimitives.StatementScopeMode ScopeMode), ModelAnalysis.StatementGraphSnapshot> snapshots) : ModelAnalysis.IStatementAnalysisService
    {
        public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey) =>
            Analyze(targetKey, ModelPrimitives.StatementScopeMode.MinimalBlock);

        public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey, ModelPrimitives.StatementScopeMode scopeMode) =>
            snapshots.TryGetValue((targetKey, scopeMode), out var snapshot)
                ? snapshot
                : new ModelAnalysis.StatementGraphSnapshot(targetKey, scopeMode, new ModelPrimitives.MemberId(string.Empty), [targetKey], []);
    }

    private sealed class StubFunctionGraphProvider : ModelAnalysis.IFunctionGraphProvider
    {
        public ModelAnalysis.FunctionGraphSnapshot GetSnapshot(ModelAnalysis.FunctionGraphRequest request) =>
            new(request.Scope, request.RootMemberIds, [], new ModelAnalysis.FunctionDependencyGraph([], []));
    }

    private sealed class StubSymbolDependencyGraphProvider : ModelAnalysis.ISymbolDependencyGraphProvider
    {
        public ModelAnalysis.SymbolDependencySlice GetForwardSlice(IReadOnlyList<string> rootSymbolIds, ModelAnalysis.SymbolDependencyQueryOptions options) =>
            new([], [], []);
    }

    private sealed class StubMethodCallQueryService : ModelAnalysis.IMethodCallQueryService
    {
        public IReadOnlyList<ModelPrimitives.MemberId> GetReachableMethods(IReadOnlyList<ModelPrimitives.MemberId> rootMemberIds) => [];
    }

    private sealed class StubDataFlowSummaryService : ModelAnalysis.IDataFlowSummaryService
    {
        public ModelAnalysis.DataFlowSummary Analyze(string targetKey) => new([], []);
    }

    private sealed class StubSwitchFlowSummaryService : ModelAnalysis.ISwitchFlowSummaryService
    {
        public ModelAnalysis.SwitchFlowSummary Analyze(string targetKey) => new([]);
    }

    private sealed class StubCallChainAnalysisService : ModelAnalysis.ICallChainAnalysisService
    {
        public ModelAnalysis.CallChainAnalysisSummary Analyze(string memberId) => new([]);
    }

    private sealed class StubAdvancedAnalysisSummaryService : ModelAnalysis.IAdvancedAnalysisSummaryService
    {
        public ModelAnalysis.AdvancedAnalysisSummary BuildSummary() => new();
    }

    private sealed class StubMemberCleanupQueryService : ModelAnalysis.IMemberCleanupQueryService
    {
        public ModelAnalysis.MemberCleanupSymbolInfo? GetSymbolInfo(string symbolId) => null;

        public ModelAnalysis.MemberCleanupTypeInfo? GetTypeInfo(string typeId) => null;

        public IReadOnlyList<ModelPrimitives.MemberId> GetReorderablePublicMethods(string typeId) => [];

        public bool HasAnyReferences(string symbolId) => false;

        public bool HasExternalMethodReferences(ModelPrimitives.MemberId memberId) => false;

        public bool HasInternalMethodReferences(ModelPrimitives.MemberId memberId) => false;
    }
}
