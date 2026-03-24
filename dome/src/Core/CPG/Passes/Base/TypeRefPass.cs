namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeRefPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (TypeDeclNode typeDecl in Context.Cpg.Nodes.OfType<TypeDeclNode>())
        {
            if (string.IsNullOrWhiteSpace(typeDecl.BaseTypeName))
            {
                continue;
            }

            string declaringTypeId = string.IsNullOrWhiteSpace(typeDecl.FullName) ? typeDecl.Name! : typeDecl.FullName;
            string typeRefId = $"type-ref:type:{declaringTypeId}:base:{typeDecl.BaseTypeName}";
            diff.AddNode(new TypeRefNode(typeRefId, typeDecl.BaseTypeName));

            TypeNode? typeNode = Context.Cpg.Nodes
                .OfType<TypeNode>()
                .FirstOrDefault(type => string.Equals(type.FullName, typeDecl.BaseTypeName, StringComparison.Ordinal));
            if (typeNode is not null)
            {
                diff.AddEdge(new CpgEdge("REF", typeRefId, typeNode.Id));
            }
        }

        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>())
        {
            if (string.IsNullOrWhiteSpace(method.ReturnTypeName))
            {
                continue;
            }

            string typeRefId = NodeIdFactory.MethodReturnTypeRef(method.ContainingTypeName, method.Name, method.ReturnTypeName);
            diff.AddNode(new TypeRefNode(typeRefId, method.ReturnTypeName));

            TypeNode? typeNode = Context.Cpg.Nodes
                .OfType<TypeNode>()
                .FirstOrDefault(type => string.Equals(type.FullName, method.ReturnTypeName, StringComparison.Ordinal));
            if (typeNode is not null)
            {
                diff.AddEdge(new CpgEdge("REF", typeRefId, typeNode.Id));
            }
        }
    }
}
