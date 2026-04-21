using System.Collections.ObjectModel;

namespace Domain.Analysis.Engine.Semantic.AccessPath;

/// <summary>
/// 表示一个访问路径及其排除扩展。
///
/// 当前实现不复刻 Joern 的完整代数运算，
/// 但保留最关键的三个能力：
/// - 表达访问路径本体；
/// - 表达“哪些扩展应被排除”；
/// - 支持追加扩展和前缀排除判断。
/// </summary>
public sealed class AccessPath : IEquatable<AccessPath>
{
    private static readonly AccessPath EmptyInstance = new(
        Array.Empty<AccessElement>(),
        Array.Empty<IReadOnlyList<AccessElement>>());

    /// <summary>
    /// 使用路径元素和排除规则初始化访问路径。
    /// </summary>
    public AccessPath(
        IEnumerable<AccessElement>? elements = null,
        IEnumerable<IReadOnlyList<AccessElement>>? exclusions = null)
    {
        Elements = new ReadOnlyCollection<AccessElement>((elements ?? Array.Empty<AccessElement>()).ToArray());
        Exclusions = new ReadOnlyCollection<IReadOnlyList<AccessElement>>(
            (exclusions ?? Array.Empty<IReadOnlyList<AccessElement>>())
            .Select(exclusion => (IReadOnlyList<AccessElement>)new ReadOnlyCollection<AccessElement>(exclusion.ToArray()))
            .ToArray());
    }

    /// <summary>
    /// 获取空访问路径。
    /// </summary>
    public static AccessPath Empty => EmptyInstance;

    /// <summary>
    /// 获取访问路径元素。
    /// </summary>
    public IReadOnlyList<AccessElement> Elements { get; }

    /// <summary>
    /// 获取被排除的扩展前缀集合。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<AccessElement>> Exclusions { get; }

    /// <summary>
    /// 当前路径是否为空。
    /// </summary>
    public bool IsEmpty => Elements.Count == 0 && Exclusions.Count == 0;

    /// <summary>
    /// 追加一段访问路径。
    /// 如果扩展命中了排除前缀，则返回空。
    /// </summary>
    public AccessPath? Append(params AccessElement[] extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Append((IReadOnlyList<AccessElement>)extension);
    }

    /// <summary>
    /// 追加一段访问路径。
    /// 如果扩展命中了排除前缀，则返回空。
    /// </summary>
    public AccessPath? Append(IReadOnlyList<AccessElement> extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (IsExtensionExcluded(extension))
        {
            return null;
        }

        return new AccessPath(Elements.Concat(extension), Exclusions);
    }

    /// <summary>
    /// 新增一个排除扩展前缀。
    /// </summary>
    public AccessPath AddExclusion(params AccessElement[] exclusion)
    {
        ArgumentNullException.ThrowIfNull(exclusion);
        return AddExclusion((IReadOnlyList<AccessElement>)exclusion);
    }

    /// <summary>
    /// 新增一个排除扩展前缀。
    /// </summary>
    public AccessPath AddExclusion(IReadOnlyList<AccessElement> exclusion)
    {
        ArgumentNullException.ThrowIfNull(exclusion);
        if (exclusion.Count == 0)
        {
            return this;
        }

        if (IsExtensionExcluded(exclusion))
        {
            return this;
        }

        List<IReadOnlyList<AccessElement>> exclusions = Exclusions.ToList();
        exclusions.Add(new ReadOnlyCollection<AccessElement>(exclusion.ToArray()));
        return new AccessPath(Elements, exclusions.OrderBy(current => string.Join("/", current.Select(item => item.ToString()))));
    }

    /// <summary>
    /// 判断某段扩展是否命中排除前缀。
    /// </summary>
    public bool IsExtensionExcluded(IReadOnlyList<AccessElement> extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Exclusions.Any(exclusion => StartsWith(extension, exclusion));
    }


    public bool Equals(AccessPath? other)
    {
        if (other is null)
        {
            return false;
        }

        return Elements.SequenceEqual(other.Elements) &&
               Exclusions.Count == other.Exclusions.Count &&
               Exclusions.Zip(other.Exclusions, (left, right) => left.SequenceEqual(right)).All(result => result);
    }


    public override bool Equals(object? obj) => obj is AccessPath other && Equals(other);


    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (AccessElement element in Elements)
        {
            hash.Add(element);
        }

        foreach (IReadOnlyList<AccessElement> exclusion in Exclusions)
        {
            foreach (AccessElement element in exclusion)
            {
                hash.Add(element);
            }
        }

        return hash.ToHashCode();
    }

    private static bool StartsWith(IReadOnlyList<AccessElement> value, IReadOnlyList<AccessElement> prefix)
    {
        if (prefix.Count > value.Count)
        {
            return false;
        }

        for (int index = 0; index < prefix.Count; index++)
        {
            if (!Equals(value[index], prefix[index]))
            {
                return false;
            }
        }

        return true;
    }
}
