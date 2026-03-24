namespace TerrariaTools.Dome.Core.Cpg;

public sealed class DeclarationTypeLinkerPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>())
        {
            AddTypeRefEdge(diff, method.Id, method.TypeFullName);
        }

        foreach (MemberNode member in Context.Cpg.Nodes.OfType<MemberNode>())
        {
            AddTypeRefEdge(diff, member.Id, member.TypeFullName);
        }

        foreach (MethodParameterInNode parameter in Context.Cpg.Nodes.OfType<MethodParameterInNode>())
        {
            AddTypeRefEdge(diff, parameter.Id, parameter.TypeFullName);
        }

        foreach (MethodParameterOutNode parameter in Context.Cpg.Nodes.OfType<MethodParameterOutNode>())
        {
            AddTypeRefEdge(diff, parameter.Id, parameter.TypeFullName);
        }

        foreach (MethodReturnNode methodReturn in Context.Cpg.Nodes.OfType<MethodReturnNode>())
        {
            AddTypeRefEdge(diff, methodReturn.Id, methodReturn.TypeFullName);
        }

        foreach (LocalNode local in Context.Cpg.Nodes.OfType<LocalNode>())
        {
            AddTypeRefEdge(diff, local.Id, local.TypeFullName);
        }
    }

    private void AddTypeRefEdge(DiffGraph diff, string sourceId, string? typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return;
        }

        TypeNode? typeNode = Context.Cpg.Nodes
            .OfType<TypeNode>()
            .FirstOrDefault(type => string.Equals(type.FullName, typeFullName, StringComparison.Ordinal));
        if (typeNode is not null)
        {
            diff.AddEdge(new CpgEdge(EdgeKinds.Ref, sourceId, typeNode.Id));
        }
    }
}
