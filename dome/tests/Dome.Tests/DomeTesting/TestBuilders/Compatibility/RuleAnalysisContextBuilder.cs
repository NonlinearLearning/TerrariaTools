using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native rule analysis contexts.
/// </summary>
internal sealed class RuleAnalysisCompatibilityContextBuilder
{
    private readonly LegacyAnalysisContextBuilder _inner = new();

    public RuleAnalysisCompatibilityContextBuilder AddTarget(ModelAnalysis.AnalysisTarget target)
    {
        _inner.AddTarget(target);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder AddEdge(ModelAnalysis.AnalysisEdge edge)
    {
        _inner.AddEdge(edge);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder AddFunctionNode(ModelAnalysis.FunctionNodeRef node)
    {
        _inner.AddFunctionNode(node);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder AddStatementSnapshot(ModelAnalysis.AnalysisTarget seedTarget, ModelPrimitives.StatementScopeMode scopeMode, params string[] nodes)
    {
        _inner.AddStatementSnapshot(seedTarget, scopeMode, nodes);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder AddStatementFacts(ModelPrimitives.MemberId memberId, params ModelAnalysis.StatementFact[] facts)
    {
        _inner.AddStatementFacts(memberId, facts);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder WithReferences(ModelAnalysis.IReferenceQueryService references)
    {
        _inner.WithReferences(references);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder WithStatements(ModelAnalysis.IStatementAnalysisService statements)
    {
        _inner.WithStatements(statements);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder WithInheritance(ModelAnalysis.IInheritanceQueryService inheritance)
    {
        _inner.WithInheritance(inheritance);
        return this;
    }

    public RuleAnalysisCompatibilityContextBuilder WithMemberCleanup(ModelAnalysis.IMemberCleanupQueryService memberCleanup)
    {
        _inner.WithMemberCleanup(memberCleanup);
        return this;
    }

    public ModelAnalysis.AnalysisContext BuildContext() => _inner.BuildContext();
}
