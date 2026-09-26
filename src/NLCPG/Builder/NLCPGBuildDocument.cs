using Microsoft.CodeAnalysis;
using NLCPG.Model;

namespace NLCPG.Builder;

/// <summary>
/// 多文件构建的一个输入文档。
/// </summary>
/// <remarks>
/// <para>
/// 两种用法：
/// </para>
/// <list type="bullet">
/// <item>
/// 只给 <see cref="FilePath"/> 与 <see cref="Source"/>（<see cref="SemanticModel"/>/<see cref="Root"/>
/// 为 <c>null</c>）：由构建器把**全部**源文件放进同一个 compilation 再解析。
/// </item>
/// <item>
/// 同时给 <see cref="SemanticModel"/> 与 <see cref="Root"/>：复用调用方**已有的**共享 compilation
/// 与语义模型。目录分析路径属于这一类——它本来就已为整个目录建好了一份 compilation，
/// 重新解析既浪费，也会让语义模型与规则管线手里的那个不是同一个实例。
/// </item>
/// </list>
/// <para>
/// ⚠ <b>同一批次内必须二选一，不允许混用</b>：一半由构建器解析、一半由调用方提供，
/// 意味着有两份互不相干的 compilation，跨文件符号即不可比。混用会显式抛异常。
/// </para>
/// </remarks>
public sealed record NLCPGBuildDocument(
  string FilePath,
  string Source,
  SemanticModel? SemanticModel = null,
  SyntaxNode? Root = null);

/// <summary>
/// 多文件构建的结果：每文件一张图，外加**整次**构建的指标。
/// </summary>
/// <remarks>
/// <see cref="Metrics"/> 是整批共享构建的指标（一次 <c>Build</c> 调用只有一份），
/// 故 <c>NodeCount</c>/<c>EdgeCount</c> 是**全批次合计**。需要单文件口径时，
/// 调用方应按 <see cref="Graphs"/> 里那张图自己的 <c>Nodes.Count</c>/<c>Edges.Count</c> 折算。
/// </remarks>
public sealed record NLCPGMultiFileBuildResult(
  IReadOnlyDictionary<string, NLCPGGraph> Graphs,
  NLCPGBuildMetrics Metrics);
