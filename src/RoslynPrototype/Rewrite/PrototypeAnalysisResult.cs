using MinimalRoslynCpg.Builder;
using RoslynPrototype.Analysis;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Rewrite;

/// <summary>
/// 封装删除原型一次完整分析与改写流程的输出结果。
/// </summary>
public sealed record PrototypeAnalysisResult(
  /// <summary>
  /// 标记阶段直接命中的种子标记。
  /// </summary>
  IReadOnlyList<MarkRecord> SeedMarks,
  /// <summary>
  /// 传播阶段派生出的传播标记。
  /// </summary>
  IReadOnlyList<PropagatedMarkRecord> PropagatedMarks,
  /// <summary>
  /// Mark Lifting 阶段派生出的结构候选标记。
  /// </summary>
  IReadOnlyList<LiftedMarkRecord> LiftedMarks,
  /// <summary>
  /// 决策阶段产出的最终改写决策。
  /// </summary>
  IReadOnlyList<RuleDecision> Decisions,
  /// <summary>
  /// 改写阶段实际产生的文本编辑。
  /// </summary>
  IReadOnlyList<RewriteEdit> Edits,
  /// <summary>
  /// 应用所有编辑后的完整源码文本。
  /// </summary>
  string? RewrittenSource,
  /// <summary>
  /// 面向调试和落盘的文本差异摘要。
  /// </summary>
  string DiffText,
  /// <summary>
  /// 若已落盘差异文件，则保存其路径；否则为空。
  /// </summary>
  string? DiffFilePath,
  /// <summary>
  /// 可选的运行统计信息。目前仅用于无引用方法目录快路径。
  /// </summary>
  AnalysisStats? Stats = null,
  /// <summary>
  /// 改写后重新编译得到的诊断。目前只收集 error 级别诊断。
  /// </summary>
  IReadOnlyList<AnalysisDiagnostic>? Diagnostics = null,
  /// <summary>
  /// 可选的分析阶段耗时明细。
  /// </summary>
  AnalysisPhaseTimings? Timings = null,
  /// <summary>
  /// 可选的 CPG 构建遥测，用于确认当前分析使用的构图配置。
  /// </summary>
  RoslynCpgBuildTelemetry? CpgBuildTelemetry = null,
  /// <summary>
  /// 可选的 Mark 阶段快照遥测，用于量化重复工作是否减少。
  /// </summary>
  MarkAnalysisTelemetry? MarkAnalysisTelemetry = null,
  /// <summary>
  /// 可选的 Structure View 结果缓存遥测，用于评估缓存命中率和保留规模。
  /// </summary>
  RoslynCpgStructureViewCacheTelemetry? StructureViewCacheTelemetry = null);

/// <summary>
/// 单次分析运行的阶段耗时明细。
/// </summary>
public sealed record AnalysisPhaseTimings(
  long PreparationMilliseconds,
  long CpgBuildMilliseconds,
  long MarkMilliseconds,
  long PropagateMilliseconds,
  long LiftMilliseconds,
  long DecideMilliseconds,
  long RewriteMilliseconds,
  long TotalMilliseconds);

/// <summary>
/// 一次分析运行的聚合统计。
/// </summary>
public sealed record AnalysisStats(
  int ScannedFileCount,
  int? AnalyzedFileCount,
  int CandidateMethodCount,
  int DeletedMethodCount,
  long ElapsedMilliseconds);

/// <summary>
/// 改写后编译诊断的稳定输出形状。
/// </summary>
public sealed record AnalysisDiagnostic(
  string Id,
  string Severity,
  string Message,
  string FilePath,
  int Start,
  int End);
