namespace Logic.Analysis.Engine.X2Cpg.DataStructures;

/// <summary>
/// 提供通用作用域栈。
///
/// 对应 Joern `Scope.scala`。查找变量时从栈顶向外层查，符合词法作用域遮蔽规则。
/// </summary>
public sealed class Scope<TIdentifier, TVariable, TScope>
    where TIdentifier : notnull
{
    private readonly List<ScopeElement<TIdentifier, TVariable, TScope>> stack = new();

    /// <summary>
    /// 当前作用域栈是否为空。
    /// </summary>
    public bool IsEmpty => stack.Count == 0;

    /// <summary>
    /// 获取作用域层数。
    /// </summary>
    public int Size => stack.Count;

    /// <summary>
    /// 推入新作用域。
    /// </summary>
    public void PushNewScope(TScope scopeNode)
    {
        stack.Insert(0, new ScopeElement<TIdentifier, TVariable, TScope>(scopeNode));
    }

    /// <summary>
    /// 弹出当前作用域。
    /// </summary>
    public TScope? PopScope()
    {
        if (stack.Count == 0)
        {
            return default;
        }

        ScopeElement<TIdentifier, TVariable, TScope> head = stack[0];
        stack.RemoveAt(0);
        return head.ScopeNode;
    }

    /// <summary>
    /// 将变量加入当前作用域。
    /// </summary>
    public TScope AddToScope(TIdentifier identifier, TVariable variable)
    {
        if (stack.Count == 0)
        {
            throw new InvalidOperationException("没有可写入的作用域。");
        }

        stack[0].AddVariable(identifier, variable);
        return stack[0].ScopeNode;
    }

    /// <summary>
    /// 从内向外查找变量。
    /// </summary>
    public TVariable? LookupVariable(TIdentifier identifier)
    {
        foreach (ScopeElement<TIdentifier, TVariable, TScope> scope in stack)
        {
            if (scope.Variables.TryGetValue(identifier, out TVariable? variable))
            {
                return variable;
            }
        }

        return default;
    }
}
