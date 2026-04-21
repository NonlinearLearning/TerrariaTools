using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend.AstModel;

/// <summary>
/// 提供前端 AST 组合的公共模板。
/// </summary>
public abstract class AstCreatorBase : AstNodeBuilder
{
    protected AstCreatorBase(CpgGraph graph, string fileName)
        : base(graph)
    {
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
    }

    public string FileName { get; }

    public abstract Ast CreateAst();

    public Ast MethodAst(CpgNode method, IEnumerable<Ast> parameters, Ast body, CpgNode methodReturn)
    {
        return Ast.FromRoot(method)
            .WithChildren(parameters)
            .WithChild(body)
            .WithChild(Ast.FromRoot(methodReturn));
    }

    public Ast BlockAst(CpgNode block, IEnumerable<Ast> children)
    {
        return Ast.FromRoot(block).WithChildren(children);
    }

    public Ast ReturnAst(CpgNode returnNode, IEnumerable<Ast> arguments)
    {
        Ast[] argumentAsts = arguments.ToArray();
        SetArgumentIndices(argumentAsts);
        return Ast.FromRoot(returnNode).WithChildren(argumentAsts);
    }

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
