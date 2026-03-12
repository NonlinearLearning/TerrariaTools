namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 分析上下文，包含分析视图和查询服务。
/// </summary>
/// <param name="View">分析视图，提供当前的分析状态。</param>
/// <param name="Inheritance">继承查询服务，用于查找类型继承关系。</param>
/// <param name="References">引用查询服务，用于查找符号引用。</param>
public sealed record AnalysisContext(
    AnalysisView View,
    IInheritanceQueryService Inheritance,
    IReferenceQueryService References);
