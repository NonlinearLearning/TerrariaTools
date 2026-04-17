using Analysis.Core;

namespace Analysis.Frontend.AstModel;

/// <summary>
/// 提供前端创建标准 CPG 节点的公共方法。
///
/// 对应 Joern `AstNodeBuilder.scala`。本地版本把常用节点属性集中在这里，
/// 减少各个 Roslyn builder 直接手写字符串属性名。
/// </summary>
public abstract class AstNodeBuilder
{
    private const int MinCodeLength = 50;
    private const int DefaultMaxCodeLength = 1000;
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化节点构建器。
    /// </summary>
    protected AstNodeBuilder(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 创建调用节点。
    /// </summary>
    public CpgNode CallNode(
        string code,
        string name,
        string methodFullName,
        string dispatchType,
        string typeFullName,
        int? line = null,
        int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.Call, code, line, column);
        node.SetProperty("Name", name);
        node.SetProperty("MethodFullName", methodFullName);
        node.SetProperty("DispatchType", dispatchType);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    /// <summary>
    /// 创建标识符节点。
    /// </summary>
    public CpgNode IdentifierNode(string name, string code, string typeFullName, int? line = null, int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.Identifier, code, line, column);
        node.SetProperty("Name", name);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    /// <summary>
    /// 创建局部变量节点。
    /// </summary>
    public CpgNode LocalNode(string name, string code, string typeFullName, int? line = null, int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.Local, code, line, column);
        node.SetProperty("Name", name);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    /// <summary>
    /// 创建方法入参节点。
    /// </summary>
    public CpgNode ParameterInNode(
        string name,
        string code,
        int index,
        string typeFullName,
        int? line = null,
        int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.MethodParameterIn, code, line, column);
        node.SetProperty("Name", name);
        node.SetProperty("Index", index);
        node.SetProperty("Order", index);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    /// <summary>
    /// 创建字面量节点。
    /// </summary>
    public CpgNode LiteralNode(string code, string typeFullName, int? line = null, int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.Literal, code, line, column);
        node.SetProperty("TypeFullName", typeFullName);
        return node;
    }

    /// <summary>
    /// 创建类型声明节点。
    /// </summary>
    public CpgNode TypeDeclNode(string name, string fullName, string code, string fileName, int? line = null, int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.TypeDecl, code, line, column);
        node.SetProperty("Name", name);
        node.SetProperty("FullName", fullName);
        node.SetProperty("FileName", fileName);
        return node;
    }

    /// <summary>
    /// 创建块节点。
    /// </summary>
    public CpgNode BlockNode(string code = "", int? line = null, int? column = null)
    {
        CpgNode node = CreateNode(CpgNodeKind.Block, code, line, column);
        node.SetProperty("TypeFullName", "ANY");
        return node;
    }

    /// <summary>
    /// 缩短过长源码，避免节点 `Code` 属性过大。
    /// </summary>
    protected static string ShortenCode(string code, int maxCodeLength = DefaultMaxCodeLength)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (maxCodeLength < 4 || code.Length < 4)
        {
            return code;
        }

        int limit = Math.Max(MinCodeLength, maxCodeLength);
        return code.Length <= limit ? code : string.Concat(code.AsSpan(0, limit - 3), "...");
    }

    private CpgNode CreateNode(CpgNodeKind kind, string code, int? line, int? column)
    {
        CpgNode node = graph.CreateNode(kind);
        node.SetProperty("Code", ShortenCode(code));
        if (line.HasValue)
        {
            node.SetProperty("Line", line.Value);
        }

        if (column.HasValue)
        {
            node.SetProperty("Column", column.Value);
        }

        return node;
    }
}
