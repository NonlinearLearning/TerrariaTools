using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native analysis contracts.
/// </summary>
internal sealed class ApplicationAnalysisCompatibilityResultBuilder
{
    private readonly LegacyAnalysisContextBuilder _inner = new();

    public ApplicationAnalysisCompatibilityResultBuilder AddTarget(ModelAnalysis.AnalysisTarget target)
    {
        _inner.AddTarget(target);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder AddEdge(ModelAnalysis.AnalysisEdge edge)
    {
        _inner.AddEdge(edge);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder AddFunctionNode(ModelAnalysis.FunctionNodeRef node)
    {
        _inner.AddFunctionNode(node);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder AddFunctionFact(ModelAnalysis.FunctionFact fact)
    {
        _inner.AddFunctionFact(fact);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder AddIncomingCaller(ModelPrimitives.MemberId callee, params ModelPrimitives.MemberId[] callers)
    {
        _inner.AddIncomingCaller(callee, callers);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder AddStatementFacts(ModelPrimitives.MemberId memberId, params ModelAnalysis.StatementFact[] facts)
    {
        _inner.AddStatementFacts(memberId, facts);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder AddStatementSnapshot(ModelAnalysis.AnalysisTarget seedTarget, ModelPrimitives.StatementScopeMode scopeMode, params string[] nodes)
    {
        _inner.AddStatementSnapshot(seedTarget, scopeMode, nodes);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithInheritance(ModelAnalysis.IInheritanceQueryService inheritance)
    {
        _inner.WithInheritance(inheritance);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithReferences(ModelAnalysis.IReferenceQueryService references)
    {
        _inner.WithReferences(references);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithStatements(ModelAnalysis.IStatementAnalysisService statements)
    {
        _inner.WithStatements(statements);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithFunctionGraphs(ModelAnalysis.IFunctionGraphProvider functionGraphs)
    {
        _inner.WithFunctionGraphs(functionGraphs);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithSymbolDependencies(ModelAnalysis.ISymbolDependencyGraphProvider symbolDependencies)
    {
        _inner.WithSymbolDependencies(symbolDependencies);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithMethodCalls(ModelAnalysis.IMethodCallQueryService methodCalls)
    {
        _inner.WithMethodCalls(methodCalls);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithDataFlow(ModelAnalysis.IDataFlowSummaryService dataFlow)
    {
        _inner.WithDataFlow(dataFlow);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithSwitchFlows(ModelAnalysis.ISwitchFlowSummaryService switchFlows)
    {
        _inner.WithSwitchFlows(switchFlows);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithCallChains(ModelAnalysis.ICallChainAnalysisService callChains)
    {
        _inner.WithCallChains(callChains);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithAdvancedAnalysis(ModelAnalysis.IAdvancedAnalysisSummaryService advancedAnalysis)
    {
        _inner.WithAdvancedAnalysis(advancedAnalysis);
        return this;
    }

    public ApplicationAnalysisCompatibilityResultBuilder WithMemberCleanup(ModelAnalysis.IMemberCleanupQueryService memberCleanup)
    {
        _inner.WithMemberCleanup(memberCleanup);
        return this;
    }

    public ApplicationAbstractions.AnalysisEngineResult BuildEngineResult(params ApplicationAbstractions.SourceDocument[] documents) => _inner.BuildEngineResult(documents);

    public ApplicationAbstractions.AnalysisEngineResult BuildApplicationResult(params ApplicationAbstractions.SourceDocument[] documents) =>
        _inner.BuildEngineResult(documents);
}

