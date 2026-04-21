namespace Logic.Analysis.Engine.X2Cpg.Utils;

/// <summary>
/// 位置和偏移转换工具。
///
/// 对应 Joern `OffsetUtils.scala`。
/// </summary>
public static class OffsetUtils
{
    public static int[] GetLineOffsetTable(string? fileContent)
    {
        if (fileContent is null)
        {
            return Array.Empty<int>();
        }

        List<int> offsets = new();
        int currentOffset = 0;
        offsets.Add(0);
        for (int index = 0; index < fileContent.Length; index++)
        {
            char current = fileContent[index];
            if (current == '\n' && index + 1 < fileContent.Length)
            {
                currentOffset = index + 1;
                offsets.Add(currentOffset);
            }
        }

        return offsets.ToArray();
    }

    public static (int Offset, int OffsetEnd) CoordinatesToOffset(
        IReadOnlyList<int> lineOffsetTable,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        ArgumentNullException.ThrowIfNull(lineOffsetTable);
        return (lineOffsetTable[startLine] + startColumn, lineOffsetTable[endLine] + endColumn + 1);
    }
}
