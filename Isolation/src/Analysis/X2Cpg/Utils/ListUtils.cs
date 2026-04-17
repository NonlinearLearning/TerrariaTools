namespace Analysis.X2Cpg.Utils;

/// <summary>
/// 列表辅助方法。
///
/// 对应 Joern `ListUtils.scala`。
/// </summary>
public static class ListUtils
{
    public static IReadOnlyList<T> TakeUntil<T>(IEnumerable<T> values, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(predicate);

        List<T> result = new();
        foreach (T value in values)
        {
            result.Add(value);
            if (predicate(value))
            {
                return result;
            }
        }

        return Array.Empty<T>();
    }

    public static T? SingleOrNone<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] array = values.Take(2).ToArray();
        return array.Length == 1 ? array[0] : default;
    }
}
