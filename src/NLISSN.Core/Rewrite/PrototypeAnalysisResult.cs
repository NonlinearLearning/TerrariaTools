using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Core.Rewrite;

/// 封装删除原型一次完整分析与改写流程的输出结果。
public sealed record PrototypeAnalysisResult(
  /// 标记阶段直接命中的种子标记。
  IReadOnlyList<MarkRecord> SeedMarks,
  /// 传播阶段派生出的传播标记。
  IReadOnlyList<PropagatedMarkRecord> PropagatedMarks,
  /// Mark Lifting 阶段派生出的结构候选标记。
  IReadOnlyList<LiftedMarkRecord> LiftedMarks,
  /// 决策阶段产出的最终改写决策。
  IReadOnlyList<RuleDecision> Decisions,
  /// 改写阶段实际产生的文本编辑。
  IReadOnlyList<RewriteEdit> Edits,
  /// 应用所有编辑后的完整源码文本。
  string? RewrittenSource,
  /// 结构化 diff 文档，作为分析结果的主 diff 载体。
  DiffDocument Diff,
  /// 若已落盘差异文件，则保存其路径；否则为空。
  string? DiffFilePath,
  /// 可选的运行统计信息。目前仅用于无引用方法目录快路径。
  AnalysisStats? Stats = null,
  /// 改写后重新编译得到的诊断。目前只收集 error 级别诊断。
  IReadOnlyList<AnalysisDiagnostic>? Diagnostics = null,
  /// 按文件保留的可回放文本操作，不等同于展示用 diff 编辑。
  IReadOnlyList<PrototypeFileRewritePlan>? RewritePlans = null)
{
  public DiffSummary DiffSummary => Diff.Summary;
}

/// 关联到单个源文件的精确改写操作。
public sealed record PrototypeFileRewritePlan(
  string FilePath,
  IReadOnlyList<RewritePlanEdit> Operations);

/// 一次分析运行的聚合统计。
public sealed record AnalysisStats(
  int ScannedFileCount,
  int? AnalyzedFileCount,
  int CandidateMethodCount,
  int DeletedMethodCount);

/// 改写后编译诊断的稳定输出形状。
public sealed record AnalysisDiagnostic(
  string Id,
  string Severity,
  string Message,
  string FilePath,
  int Start,
  int End);
