using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis.Dome;
using QuikGraph;

namespace TerrariaTools.Rules.Dome.Mark.ContextRules;

/// <summary>
/// 传播上下文，包含图、语义模型等必要信息
/// </summary>
public class SpreadingContext
{
    public DataFlowDependencyGraph? Graph { get; set; }
    public SemanticModel? SemanticModel { get; set; }
    public BidirectionalGraph<ISymbol, InheritanceEdge>? InheritanceGraph { get; set; }
}

/// <summary>
/// 传播规则接口
/// </summary>
public interface ISpreadingRule
{
    /// <summary>
    /// 执行传播逻辑
    /// </summary>
    PropagationResult Propagate(DataFlowDependencyNode source, DataFlowDependencyNode target, DataFlowDependencyEdge edge, SpreadingContext context);
}

/// <summary>
/// 传播结果
/// </summary>
public class PropagationResult
{
    /// <summary>
    /// 是否应该传播（加入队列）
    /// </summary>
    public bool ShouldPropagate { get; set; }

    /// <summary>
    /// 是否已处理（如果是，则跳过后续低优先级规则）
    /// </summary>
    public bool IsHandled { get; set; }

    /// <summary>
    /// 静态方法：不传播
    /// </summary>
    public static PropagationResult None => new() { ShouldPropagate = false, IsHandled = false };

    /// <summary>
    /// 静态方法：传播且未处理（允许后续规则继续执行）
    /// </summary>
    public static PropagationResult Propagate => new() { ShouldPropagate = true, IsHandled = false };

    /// <summary>
    /// 静态方法：传播且已处理（阻止后续低优先级规则）
    /// </summary>
    public static PropagationResult Handled => new() { ShouldPropagate = true, IsHandled = true };

    /// <summary>
    /// 静态方法：不传播且已处理（熔断）
    /// </summary>
    public static PropagationResult Blocked => new() { ShouldPropagate = false, IsHandled = true };
}
