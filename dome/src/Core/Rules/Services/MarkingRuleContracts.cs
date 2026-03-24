namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 定义从显式种子目标生成初始决策的规则契约。
/// </summary>
public interface ISeedRule
{
    /// <summary>
    /// 评估指定目标并生成种子决策。
    /// </summary>
    /// <param name="target">待评估的分析目标。</param>
    /// <returns>生成的种子决策序列。</returns>
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisTarget target);
}

/// <summary>
/// 定义控制数据流传播是否允许继续的规则契约。
/// </summary>
public interface IPropagationRule
{
    /// <summary>
    /// 判断源决策是否可以沿着当前符号传播到目标。
    /// </summary>
    /// <param name="target">候选传播目标。</param>
    /// <param name="usedSymbol">连接源与目标的使用符号。</param>
    /// <param name="sourceDecision">传播来源决策。</param>
    /// <returns>允许传播时返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    bool CanPropagate(ModelAnalysis.AnalysisTarget target, ModelAnalysis.SymbolRef usedSymbol, ModelRules.MarkDecision sourceDecision);
}

/// <summary>
/// 定义保护目标不被规则处理的规则契约。
/// </summary>
public interface IProtectionRule
{
    /// <summary>
    /// 判断指定目标是否应当被保护并跳过。
    /// </summary>
    /// <param name="target">待评估的分析目标。</param>
    /// <returns>需要跳过时返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    bool Blocks(ModelAnalysis.AnalysisTarget target);
}

/// <summary>
/// 定义将表达式级标记投影到语句级目标的规则契约。
/// </summary>
public interface IExpressionProjectionRule
{
    /// <summary>
    /// 评估指定目标并生成投影决策。
    /// </summary>
    /// <param name="target">待评估的分析目标。</param>
    /// <returns>生成的投影决策序列。</returns>
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisTarget target);
}

/// <summary>
/// 定义针对方法节点生成决策的规则契约。
/// </summary>
public interface IMethodRule
{
    /// <summary>
    /// 评估指定方法节点并生成决策。
    /// </summary>
    /// <param name="context">当前分析上下文。</param>
    /// <param name="functionNode">待评估的方法节点。</param>
    /// <returns>生成的决策序列。</returns>
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode);
}

/// <summary>
/// 定义针对字段或属性目标生成决策的规则契约。
/// </summary>
public interface IMemberTargetRule
{
    /// <summary>
    /// 评估指定成员目标并生成决策。
    /// </summary>
    /// <param name="context">当前分析上下文。</param>
    /// <param name="target">待评估的成员目标。</param>
    /// <returns>生成的决策序列。</returns>
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target);
}

/// <summary>
/// 定义针对类型目标生成决策的规则契约。
/// </summary>
public interface IClassRule
{
    /// <summary>
    /// 评估指定类型目标并生成决策。
    /// </summary>
    /// <param name="context">当前分析上下文。</param>
    /// <param name="target">待评估的类型目标。</param>
    /// <returns>生成的决策序列。</returns>
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target);
}

/// <summary>
/// 定义将语句级决策提升到边界外成员级的规则契约。
/// </summary>
public interface IBoundaryPromotionRule
{
    /// <summary>
    /// 评估指定决策是否需要执行边界提升。
    /// </summary>
    /// <param name="context">当前分析上下文。</param>
    /// <param name="target">原始语句目标。</param>
    /// <param name="decision">原始决策。</param>
    /// <returns>生成的提升后决策序列。</returns>
    IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target, ModelRules.MarkDecision decision);
}

/// <summary>
/// 定义为种子目标选择语句作用域模式的规则契约。
/// </summary>
public interface IStatementScopeRule
{
    /// <summary>
    /// 为指定种子目标选择传播时使用的语句作用域模式。
    /// </summary>
    /// <param name="context">当前分析上下文。</param>
    /// <param name="seedTarget">传播起点目标。</param>
    /// <returns>选中的语句作用域模式。</returns>
    ModelPrimitives.StatementScopeMode SelectScopeMode(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget seedTarget);
}
