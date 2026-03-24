using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Core.Cpg;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

internal sealed class ApplicationNativeAnalysisResultBuilder
{
    private readonly List<ModelAnalysis.AnalysisTarget> _targets = [];
    private readonly List<ModelAnalysis.AnalysisEdge> _edges = [];

    public ApplicationNativeAnalysisResultBuilder AddTarget(ModelAnalysis.AnalysisTarget target)
    {
        _targets.Add(target);
        return this;
    }

    public ApplicationNativeAnalysisResultBuilder AddEdge(ModelAnalysis.AnalysisEdge edge)
    {
        _edges.Add(edge);
        return this;
    }

    public ModelAnalysis.AnalysisOutput Build(params ModelAnalysis.SourceDocument[] documents)
    {
        var view = new ModelAnalysis.AnalysisResultModel(
            _targets.ToArray(),
            _edges.ToArray(),
            new ModelAnalysis.TypeDependencyGraph([], []),
            new ModelAnalysis.FunctionDependencyGraph([], []),
            new ModelAnalysis.StatementDependencyGraph([], []),
            ModelPrimitives.StatementGraphMaterialization.None,
            ModelPrimitives.FunctionGraphMaterialization.None);
        var snapshot = new ModelAnalysis.AnalysisExecutionSnapshot(
            view,
            ModelAnalysis.FunctionIndex.Empty,
            ModelAnalysis.FunctionFactsIndex.Empty,
            BuildStatementFacts(_targets),
            new DomeCpg());

        return new ModelAnalysis.AnalysisOutput(
            view,
            snapshot,
            BuildServices(),
            new ModelAnalysis.AnalysisPerformanceSummary(
                documents.Length,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero));
    }

    private static ModelAnalysis.StatementFactsIndex BuildStatementFacts(IEnumerable<ModelAnalysis.AnalysisTarget> targets)
    {
        var facts = targets
            .Where(static target => target.Target.TargetKind == ModelPrimitives.TargetKind.Statement)
            .ToDictionary(
                static target => target.Locator.TargetKey,
                static target => new ModelAnalysis.StatementFact(
                    target.Locator.TargetKey,
                    target.Target.MemberId,
                    target.StatementKind,
                    target.DefinesSymbols,
                    target.UsesSymbols,
                    target.InvokedMemberIds,
                    target.ScopeMode,
                    target.ScopeId,
                    target.ParentScopeId,
                    target.Locator.SpanStart,
                    target.Locator.SpanLength),
                StringComparer.Ordinal);

        return new ModelAnalysis.StatementFactsIndex(facts);
    }

    private static ModelAnalysis.AnalysisServices BuildServices() =>
        new(
            new StubInheritanceQueryService(),
            new StubReferenceQueryService(),
            new StubStatementAnalysisService(),
            new StubFunctionGraphProvider(),
            new StubSymbolDependencyGraphProvider(),
            new StubMethodCallQueryService(),
            new StubDataFlowSummaryService(),
            new StubSwitchFlowSummaryService(),
            new StubCallChainAnalysisService(),
            new StubAdvancedAnalysisSummaryService(),
            new StubMemberCleanupQueryService());

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

    private sealed class StubStatementAnalysisService : ModelAnalysis.IStatementAnalysisService
    {
        public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey) =>
            new(targetKey, ModelPrimitives.StatementScopeMode.MinimalBlock, new ModelPrimitives.MemberId(string.Empty), [targetKey], []);

        public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey, ModelPrimitives.StatementScopeMode scopeMode) =>
            new(targetKey, scopeMode, new ModelPrimitives.MemberId(string.Empty), [targetKey], []);
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

        public bool HasAnyReferences(string symbolId) => false;

        public bool HasInternalMethodReferences(ModelPrimitives.MemberId memberId) => false;

        public bool HasExternalMethodReferences(ModelPrimitives.MemberId memberId) => false;

        public IReadOnlyList<ModelPrimitives.MemberId> GetReorderablePublicMethods(string typeId) => [];
    }
}




