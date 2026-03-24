namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeEvalPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (CallNode call in Context.Cpg.Nodes.OfType<CallNode>())
        {
            if (string.IsNullOrWhiteSpace(call.TypeFullName))
            {
                continue;
            }

            TypeNode? typeNode = Context.Cpg.Nodes
                .OfType<TypeNode>()
                .FirstOrDefault(type => string.Equals(type.FullName, call.TypeFullName, StringComparison.Ordinal));
            if (typeNode is not null)
            {
                diff.AddEdge(new CpgEdge("EVAL_TYPE", call.Id, typeNode.Id));
            }
        }
    }
}
