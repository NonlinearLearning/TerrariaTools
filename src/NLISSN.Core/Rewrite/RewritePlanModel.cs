using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Text;

namespace NLISSN.Core.Rewrite;

/// 一项可移植的纯文本改写操作。其中刻意不包含 Roslyn 对象。
public sealed record RewritePlanEdit(
  int Start,
  int Length,
  string OriginalText,
  string ReplacementText)
{
  [JsonIgnore]
  public TextSpan Span => new(Start, Length);
}

/// 一个源文件相对于工件输入根目录的改写操作。
public sealed record RewritePlanFile(
  string RelativePath,
  string SourceSha256,
  IReadOnlyList<RewritePlanEdit> Edits);

/// 改写计划工件的带版本项目级元数据。
public sealed record RewritePlanManifest(
  int SchemaVersion,
  string Operation,
  string InputRoot,
  int SourceFileCount,
  int PlannedFileCount,
  string PlanFile,
  string PlanSha256);
