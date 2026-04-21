using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Language.DataFlow;

/// <summary>
/// 表示一条可解释的数据流路径。
///
/// 对应 Joern `Path.scala`。这里保留核心能力：节点列表、源码展示对和简单表格渲染。
/// </summary>
public sealed class Path
{
    /// <summary>
    /// 使用路径节点初始化对象。
    /// </summary>
    public Path(IEnumerable<CpgNode> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Elements = elements.ToArray();
    }

    /// <summary>
    /// 获取路径上的节点。
    /// </summary>
    public IReadOnlyList<CpgNode> Elements { get; }

    /// <summary>
    /// 返回适合展示的代码和行号。
    /// </summary>
    public IReadOnlyList<(string Code, int? Line)> ResultPairs()
    {
        return Elements.Select(node => (Code: DisplayCode(node), Line: Line(node)))
            .WhereConsecutiveDistinct()
            .ToArray();
    }


    public override string ToString()
    {
        string[] rows = Elements.Select(node =>
        {
            string line = Line(node)?.ToString() ?? "N/A";
            return $"{node.Kind}\t{DisplayCode(node)}\t{line}";
        }).ToArray();
        return string.Join(Environment.NewLine, rows);
    }

    private static string DisplayCode(CpgNode node)
    {
        if (node.TryGetProperty<string>("Code", out string? code) && !string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        if (node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return node.Kind.ToString();
    }

    private static int? Line(CpgNode node)
    {
        return node.TryGetProperty<int>("Line", out int line) ? line : null;
    }
}

internal static class PathEnumerableExtensions
{
    public static IEnumerable<T> WhereConsecutiveDistinct<T>(this IEnumerable<T> values)
    {
        bool hasPrevious = false;
        T? previous = default;
        foreach (T value in values)
        {
            if (!hasPrevious || !EqualityComparer<T>.Default.Equals(previous, value))
            {
                yield return value;
            }

            previous = value;
            hasPrevious = true;
        }
    }
}
