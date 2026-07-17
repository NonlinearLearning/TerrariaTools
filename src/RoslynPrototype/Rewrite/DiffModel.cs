using Microsoft.CodeAnalysis.Text;

namespace RoslynPrototype.Rewrite;

public enum DiffEditKind
{
  Delete = 0,
  Insert = 1,
  Replace = 2
}

public enum DiffLineKind
{
  Meta = 0,
  Context = 1,
  Delete = 2,
  Insert = 3,
  ReplaceOld = 4,
  ReplaceNew = 5
}

public sealed record DiffLine(
  DiffLineKind Kind,
  string Text);

public sealed record DiffBlock(
  IReadOnlyList<DiffLine> Lines);

public sealed record DiffSection(
  string FilePath,
  TextSpan Span,
  int EditIndex,
  DiffEditKind EditKind,
  string OriginalText,
  string ReplacementText,
  IReadOnlyList<DiffBlock> Blocks);

public sealed record DiffFile(
  string FilePath,
  IReadOnlyList<DiffSection> Sections);

public sealed record DiffSummary(
  int FileCount,
  int EditCount,
  int SectionCount,
  int BlockCount,
  int InsertLineCount,
  int DeleteLineCount,
  int ReplaceBlockCount)
{
  public static DiffSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

public sealed record DiffDocument(
  IReadOnlyList<DiffFile> Files,
  DiffSummary Summary)
{
  public static DiffDocument Empty { get; } = new(Array.Empty<DiffFile>(), DiffSummary.Empty);

  public static implicit operator string(DiffDocument document)
  {
    return document.ToString();
  }

  public override string ToString()
  {
    return new TextDiffRenderer().RenderLegacy(this);
  }
}
