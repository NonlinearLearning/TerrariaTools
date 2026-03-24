namespace TerrariaTools.Dome.Core.Cpg;

public abstract class StoredNode(string id)
{
    public string Id { get; } = id;
}

public abstract class AstNode(string id) : StoredNode(id)
{
}

public abstract class ExpressionNode(string id) : AstNode(id), ICfgNode
{
}
