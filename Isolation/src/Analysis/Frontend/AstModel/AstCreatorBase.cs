using Analysis.Core;

namespace Analysis.Frontend.AstModel;

/// <summary>
/// 提供前端 AST 组合的公共模板。
///
/// 对应 Joern `AstCreatorBase.scala`。当前 Roslyn 前端已有独立 builder，
/// 这个类型用于把 Joern 的方法级、控制结构级组合方式沉淀成可复用抽象。
/// </summary>
public abstract class AstCreatorBase : AstNodeBuilder
{
    /// <summary>
    /// 使用目标图和文件名初始化 AST 创建器。
    /// </summary>
    protected AstCreatorBase(CpgGraph graph, string fileName)
        : base(graph)
    {
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
    }

    /// <summary>
    /// 获取当前处理的文件名。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 创建完整 AST。
    /// </summary>
    public abstract Ast CreateAst();

    /// <summary>
    /// 创建方法 AST。
    /// </summary>
    public Ast MethodAst(CpgNode method, IEnumerable<Ast> parameters, Ast body, CpgNode methodReturn)
    {
        return Ast.FromRoot(method)
            .WithChildren(parameters)
            .WithChild(body)
            .WithChild(Ast.FromRoot(methodReturn));
    }

    /// <summary>
    /// 创建块 AST。
    /// </summary>
    public Ast BlockAst(CpgNode block, IEnumerable<Ast> children)
    {
        return Ast.FromRoot(block).WithChildren(children);
    }

    /// <summary>
    /// 创建 return AST，并给参数补 `ArgumentIndex`。
    /// </summary>
    public Ast ReturnAst(CpgNode returnNode, IEnumerable<Ast> arguments)
    {
        Ast[] argumentAsts = arguments.ToArray();
        SetArgumentIndices(argumentAsts);
        return Ast.FromRoot(returnNode).WithChildren(argumentAsts);
    }

    /// <summary>
    /// 创建控制结构 AST。
    /// </summary>
    public Ast ControlStructureAst(CpgNode controlNode, Ast? condition, IEnumerable<Ast> children)
    {
        List<Ast> orderedChildren = new();
        if (condition is not null)
        {
            orderedChildren.Add(condition);
        }

        orderedChildren.AddRange(children);
        Ast ast = Ast.FromRoot(controlNode).WithChildren(orderedChildren);
        return condition?.Root is null ? ast : ast.WithConditionEdge(controlNode, condition.Root);
    }

    /// <summary>
    /// 创建调用 AST，并补参数边和 receiver 边。
    /// </summary>
    public Ast CallAst(CpgNode callNode, IEnumerable<Ast> arguments, Ast? receiver = null)
    {
        Ast[] argumentAsts = arguments.ToArray();
        SetArgumentIndices(argumentAsts);
        Ast ast = Ast.FromRoot(callNode)
            .WithChildren(receiver is null ? argumentAsts : new[] { receiver }.Concat(argumentAsts));

        if (receiver?.Root is not null)
        {
            receiver.Root.SetProperty("ArgumentIndex", 0);
            ast = ast.WithReceiverEdge(callNode, receiver.Root);
        }

        return ast.WithArgEdges(callNode, argumentAsts.Select(argument => argument.Root).OfType<CpgNode>());
    }

    private static void SetArgumentIndices(IReadOnlyList<Ast> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            CpgNode? root = arguments[index].Root;
            root?.SetProperty("ArgumentIndex", index + 1);
        }
    }
}
