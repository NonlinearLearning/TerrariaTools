namespace Logic.Analysis.Engine.X2Cpg.DataStructures;

/// <summary>
/// 提供 Joern x2cpg 风格的栈。
///
/// 对应 Joern `datastructures/Stack.scala`：`Push` 从头部加入，`Pop` 从头部移除。
/// </summary>
public sealed class X2CpgStack<T>
{
    private readonly List<T> items = new();

    /// <summary>
    /// 获取当前元素数量。
    /// </summary>
    public int Count => items.Count;

    /// <summary>
    /// 从栈顶压入元素。
    /// </summary>
    public void Push(T item)
    {
        items.Insert(0, item);
    }

    /// <summary>
    /// 弹出栈顶元素。
    /// </summary>
    public T Pop()
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("栈为空。");
        }

        T item = items[0];
        items.RemoveAt(0);
        return item;
    }
}
