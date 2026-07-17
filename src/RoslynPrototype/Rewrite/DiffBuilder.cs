namespace RoslynPrototype.Rewrite;

public sealed class DiffBuilder
{
  public DiffDocument Build(IReadOnlyList<RewriteEdit> edits)
  {
    if (edits.Count == 0) {
      return DiffDocument.Empty;
    }

    var sectionsByFile = new Dictionary<string, List<DiffSection>>(StringComparer.Ordinal);
    var fileOrder = new List<string>();
    var insertLineCount = 0;
    var deleteLineCount = 0;
    var replaceBlockCount = 0;

    for (var index = 0; index < edits.Count; index++) {
      var edit = edits[index];
      var editKind = ResolveEditKind(edit);
      var block = BuildBlock(edit, editKind);
      var section = new DiffSection(
        edit.FilePath,
        edit.Span,
        index,
        editKind,
        edit.OriginalText,
        edit.ReplacementText,
        new[] { block });
      if (!sectionsByFile.TryGetValue(edit.FilePath, out var sections)) {
        sections = new List<DiffSection>();
        sectionsByFile[edit.FilePath] = sections;
        fileOrder.Add(edit.FilePath);
      }

      sections.Add(section);
      insertLineCount += CountLines(edit.ReplacementText);
      deleteLineCount += CountLines(edit.OriginalText);
      if (editKind == DiffEditKind.Replace) {
        replaceBlockCount += 1;
      }
    }

    var files = fileOrder
      .Select(filePath => new DiffFile(filePath, sectionsByFile[filePath]))
      .ToList();
    var blockCount = files.Sum(file => file.Sections.Sum(section => section.Blocks.Count));
    var sectionCount = files.Sum(file => file.Sections.Count);
    return new DiffDocument(
      files,
      new DiffSummary(
        files.Count,
        edits.Count,
        sectionCount,
        blockCount,
        insertLineCount,
        deleteLineCount,
        replaceBlockCount));
  }

  public DiffDocument Combine(IEnumerable<DiffDocument> documents)
  {
    var files = documents
      .SelectMany(document => document.Files)
      .OrderBy(file => file.FilePath, StringComparer.Ordinal)
      .Select(file => new DiffFile(
        file.FilePath,
        file.Sections
          .OrderBy(section => section.EditIndex)
          .ToList()))
      .ToList();
    if (files.Count == 0) {
      return DiffDocument.Empty;
    }

    var summary = new DiffSummary(
      files.Count,
      files.Sum(file => file.Sections.Count),
      files.Sum(file => file.Sections.Count),
      files.Sum(file => file.Sections.Sum(section => section.Blocks.Count)),
      files.Sum(file => file.Sections.Sum(section => CountLines(section.ReplacementText))),
      files.Sum(file => file.Sections.Sum(section => CountLines(section.OriginalText))),
      files.Sum(file => file.Sections.Count(section => section.EditKind == DiffEditKind.Replace)));
    return new DiffDocument(files, summary);
  }

  private static DiffBlock BuildBlock(RewriteEdit edit, DiffEditKind editKind)
  {
    return editKind switch
    {
      DiffEditKind.Delete => new DiffBlock(new[]
      {
        new DiffLine(DiffLineKind.Delete, edit.OriginalText),
        new DiffLine(DiffLineKind.Insert, string.Empty)
      }),
      DiffEditKind.Insert => new DiffBlock(new[]
      {
        new DiffLine(DiffLineKind.Insert, edit.ReplacementText)
      }),
      _ => new DiffBlock(new[]
      {
        new DiffLine(DiffLineKind.ReplaceOld, edit.OriginalText),
        new DiffLine(DiffLineKind.ReplaceNew, edit.ReplacementText)
      })
    };
  }

  private static DiffEditKind ResolveEditKind(RewriteEdit edit)
  {
    if (string.IsNullOrEmpty(edit.OriginalText)) {
      return DiffEditKind.Insert;
    }

    if (string.IsNullOrEmpty(edit.ReplacementText)) {
      return DiffEditKind.Delete;
    }

    return DiffEditKind.Replace;
  }

  private static int CountLines(string text)
  {
    if (string.IsNullOrEmpty(text)) {
      return 0;
    }

    return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;
  }
}
