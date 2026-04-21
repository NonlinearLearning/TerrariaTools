namespace Logic.Analysis.Engine.X2Cpg.DataStructures;

/// <summary>
/// 表示作用域栈中的一个元素。
///
/// 对应 Joern `ScopeElement.scala` 的核心职责：保存作用域节点和本层变量表。
/// </summary>
public sealed class ScopeElement<TIdentifier, TVariable, TScope>
    where TIdentifier : notnull
{
    private readonly Dictionary<TIdentifier, TVariable> variables = new();

    /// <summary>
    /// 使用作用域节点初始化元素。
    /// </summary>
    public ScopeElement(TScope scopeNode)
    {
        ScopeNode = scopeNode;
    }

    /// <summary>
    /// 获取作用域节点。
    /// </summary>
    public TScope ScopeNode { get; }

    /// <summary>
    /// 获取本层变量表。
    /// </summary>
    public IReadOnlyDictionary<TIdentifier, TVariable> Variables => variables;

    /// <summary>
    /// 添加或替换变量。
    /// </summary>
    public void AddVariable(TIdentifier identifier, TVariable variable)
    {
        variables[identifier] = variable;
    }
}
