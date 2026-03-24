using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Tests.Testing.TestBuilders;

/// <summary>
/// 用于构建规则分析上下文的兼容性构建器。
/// 通过内部构建器组装测试所需的分析数据。
/// </summary>
internal sealed class RuleAnalysisCompatibilityContextBuilder
{
    private readonly CompatibilityAnalysisContextBuilder _inner = new();

    /// <summary>
    /// 添加分析目标。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder AddTarget(ModelAnalysis.AnalysisTarget target)
    {
        _inner.AddTarget(target);
        return this;
    }

    /// <summary>
    /// 添加分析边。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder AddEdge(ModelAnalysis.AnalysisEdge edge)
    {
        _inner.AddEdge(edge);
        return this;
    }

    /// <summary>
    /// 添加函数节点。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder AddFunctionNode(ModelAnalysis.FunctionNodeRef node)
    {
        _inner.AddFunctionNode(node);
        return this;
    }

    /// <summary>
    /// 添加语句快照。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder AddStatementSnapshot(ModelAnalysis.AnalysisTarget seedTarget, ModelPrimitives.StatementScopeMode scopeMode, params string[] nodes)
    {
        _inner.AddStatementSnapshot(seedTarget, scopeMode, nodes);
        return this;
    }

    /// <summary>
    /// 添加语句事实。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder AddStatementFacts(ModelPrimitives.MemberId memberId, params ModelAnalysis.StatementFact[] facts)
    {
        _inner.AddStatementFacts(memberId, facts);
        return this;
    }

    /// <summary>
    /// 设置引用查询服务。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder WithReferences(ModelAnalysis.IReferenceQueryService references)
    {
        _inner.WithReferences(references);
        return this;
    }

    /// <summary>
    /// 设置语句分析服务。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder WithStatements(ModelAnalysis.IStatementAnalysisService statements)
    {
        _inner.WithStatements(statements);
        return this;
    }

    /// <summary>
    /// 设置继承查询服务。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder WithInheritance(ModelAnalysis.IInheritanceQueryService inheritance)
    {
        _inner.WithInheritance(inheritance);
        return this;
    }

    /// <summary>
    /// 设置成员清理查询服务。
    /// </summary>
    public RuleAnalysisCompatibilityContextBuilder WithMemberCleanup(ModelAnalysis.IMemberCleanupQueryService memberCleanup)
    {
        _inner.WithMemberCleanup(memberCleanup);
        return this;
    }

    /// <summary>
    /// 构建分析上下文。
    /// </summary>
    public ModelAnalysis.AnalysisContext BuildContext() => _inner.BuildContext();
}

