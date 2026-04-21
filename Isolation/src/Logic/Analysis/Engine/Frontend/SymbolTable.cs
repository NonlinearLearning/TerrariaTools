namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 表示符号表里的键。
///
/// 这里参考 Joern `SymbolTable.scala` 的做法，保留“局部变量”“集合变量”
/// “调用别名”这三种最核心键类型。当前实现先服务于阶段二类型恢复与导入解析，
/// 不强行绑定具体 AST 节点类型。
/// </summary>
public abstract record SymbolTableKey(string Identifier);

/// <summary>
/// 表示局部变量键。
/// </summary>
public sealed record LocalVariableKey(string Identifier) : SymbolTableKey(Identifier);

/// <summary>
/// 表示集合变量键。
/// </summary>
public sealed record CollectionVariableKey(string Identifier, string Index) : SymbolTableKey(Identifier);

/// <summary>
/// 表示调用别名键。
/// </summary>
public sealed record CallAliasKey(string Identifier, string? ReceiverName = null) : SymbolTableKey(Identifier);

/// <summary>
/// 表示最小符号表。
///
/// 这个实现保留了 Joern 原版最关键的语义：
/// - 一个键可以对应多个候选类型；
/// - 写入时要做上限控制；
/// - 优先保留真实类型，最后才保留占位类型。
/// </summary>
/// <typeparam name="TKey">符号表键类型。</typeparam>
public sealed class SymbolTable<TKey> where TKey : SymbolTableKey
{
    private readonly Dictionary<TKey, HashSet<string>> table = new();
    private readonly Func<string, bool> isDummyType;
    private readonly int setLimit;

    /// <summary>
    /// 初始化符号表。
    /// </summary>
    /// <param name="isDummyType">判断类型名是否为占位类型的函数。</param>
    /// <param name="setLimit">每个键允许保留的最大候选类型数。</param>
    public SymbolTable(Func<string, bool>? isDummyType = null, int setLimit = 10)
    {
        this.isDummyType = isDummyType ?? (_ => false);
        this.setLimit = setLimit > 0 ? setLimit : 10;
    }

    /// <summary>
    /// 获取当前键数量。
    /// </summary>
    public int Count => table.Count;

    /// <summary>
    /// 判断是否包含指定键。
    /// </summary>
    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return table.ContainsKey(key);
    }

    /// <summary>
    /// 获取指定键的全部候选类型。
    /// </summary>
    public IReadOnlyCollection<string> Get(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return table.TryGetValue(key, out HashSet<string>? values)
            ? values.ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// 使用新类型集合覆盖指定键。
    /// </summary>
    public IReadOnlyCollection<string> Put(TKey key, IEnumerable<string> typeFullNames)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(typeFullNames);

        string[] normalized = Normalize(typeFullNames).ToArray();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        table[key] = normalized.ToHashSet(StringComparer.Ordinal);
        return normalized;
    }

    /// <summary>
    /// 向指定键追加候选类型。
    /// </summary>
    public IReadOnlyCollection<string> Append(TKey key, IEnumerable<string> typeFullNames)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(typeFullNames);

        string[] normalized = Normalize(typeFullNames).ToArray();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        if (!table.TryGetValue(key, out HashSet<string>? current))
        {
            table[key] = normalized.ToHashSet(StringComparer.Ordinal);
            return normalized;
        }

        string[] merged = Normalize(current.Concat(normalized)).ToArray();
        table[key] = merged.ToHashSet(StringComparer.Ordinal);
        return merged;
    }

    /// <summary>
    /// 删除一个键并返回原值。
    /// </summary>
    public IReadOnlyCollection<string> Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!table.Remove(key, out HashSet<string>? values))
        {
            return Array.Empty<string>();
        }

        return values.ToArray();
    }

    /// <summary>
    /// 返回当前全部条目的快照。
    /// </summary>
    public IReadOnlyCollection<KeyValuePair<TKey, IReadOnlyCollection<string>>> Snapshot()
    {
        return table
            .Select(pair => new KeyValuePair<TKey, IReadOnlyCollection<string>>(
                pair.Key,
                pair.Value.ToArray()))
            .ToArray();
    }

    private IEnumerable<string> Normalize(IEnumerable<string> typeFullNames)
    {
        string[] allTypes = typeFullNames
            .Where(static typeFullName => !string.IsNullOrWhiteSpace(typeFullName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        IEnumerable<string> concreteTypes = allTypes.Where(typeFullName => !isDummyType(typeFullName));
        IEnumerable<string> dummyTypes = allTypes.Where(typeFullName => isDummyType(typeFullName));
        return concreteTypes.Concat(dummyTypes).Take(setLimit);
    }
}
