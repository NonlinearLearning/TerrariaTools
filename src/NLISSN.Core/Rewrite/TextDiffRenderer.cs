using System.Text;

namespace NLISSN.Core.Rewrite;

public sealed class TextDiffRenderer
{
  // 按调用方指定的视图名称渲染整个文档级 diff。
  public string Render(DiffDocument document, string view)
  {
    return string.Equals(view, "readable", StringComparison.OrdinalIgnoreCase)
      ? RenderReadable(document)
      : RenderLegacy(document);
  }

  // 按调用方指定的视图名称渲染单文件 diff。
  public string Render(DiffFile file, string view)
  {
    return string.Equals(view, "readable", StringComparison.OrdinalIgnoreCase)
      ? RenderReadable(file)
      : RenderLegacy(file);
  }

  // 输出兼容旧 CLI 行为的文档级文本 diff。
  public string RenderLegacy(DiffDocument document)
  {
    if (document.Files.Count == 0) {
      return string.Empty;
    }

    if (document.Files.Count == 1) {
      return RenderLegacy(document.Files[0]);
    }

    var builder = new StringBuilder();
    for (var fileIndex = 0; fileIndex < document.Files.Count; fileIndex++) {
      var file = document.Files[fileIndex];
      builder.AppendLine($"### {file.FilePath}");
      builder.Append(RenderLegacy(file));
      if (fileIndex + 1 < document.Files.Count) {
        builder.AppendLine();
        builder.AppendLine();
      }
    }

    return builder.ToString();
  }

  // 输出兼容旧 CLI 行为的单文件文本 diff。
  public string RenderLegacy(DiffFile file)
  {
    if (file.Sections.Count == 0) {
      return string.Empty;
    }

    var builder = new StringBuilder();
    for (var index = 0; index < file.Sections.Count; index++) {
      var section = file.Sections[index];
      builder.AppendLine(
        $"--- original #{section.EditIndex + 1} {section.FilePath}:{section.Span.Start}..{section.Span.End}");
      builder.AppendLine(section.OriginalText);
      builder.AppendLine($"+++ rewritten #{section.EditIndex + 1}");
      builder.AppendLine(string.IsNullOrEmpty(section.ReplacementText)
        ? "<deleted>"
        : section.ReplacementText);
      if (index + 1 < file.Sections.Count) {
        builder.AppendLine();
      }
    }

    return builder.ToString();
  }

  // 输出带汇总头和可读标签的文档级 diff。
  public string RenderReadable(DiffDocument document)
  {
    if (document.Files.Count == 0) {
      return string.Empty;
    }

    var builder = new StringBuilder();
    builder.AppendLine(
      $"diff-summary files={document.Summary.FileCount} edits={document.Summary.EditCount} " +
      $"blocks={document.Summary.BlockCount}");
    builder.AppendLine();
    for (var fileIndex = 0; fileIndex < document.Files.Count; fileIndex++) {
      builder.Append(RenderReadable(document.Files[fileIndex]));
      if (fileIndex + 1 < document.Files.Count) {
        builder.AppendLine();
        builder.AppendLine();
      }
    }

    return builder.ToString();
  }

  // 输出更适合人工阅读的单文件 diff 片段。
  public string RenderReadable(DiffFile file)
  {
    if (file.Sections.Count == 0) {
      return string.Empty;
    }

    var builder = new StringBuilder();
    builder.AppendLine($"=== file {file.FilePath}");
    for (var index = 0; index < file.Sections.Count; index++) {
      var section = file.Sections[index];
      builder.AppendLine(
        $"edit #{section.EditIndex + 1} kind={section.EditKind} span={section.Span.Start}..{section.Span.End}");
      builder.AppendLine("--- before");
      builder.AppendLine(string.IsNullOrEmpty(section.OriginalText) ? "<empty>" : section.OriginalText);
      builder.AppendLine("+++ after");
      builder.AppendLine(string.IsNullOrEmpty(section.ReplacementText)
        ? "<deleted>"
        : section.ReplacementText);
      if (index + 1 < file.Sections.Count) {
        builder.AppendLine();
      }
    }

    return builder.ToString();
  }
}
