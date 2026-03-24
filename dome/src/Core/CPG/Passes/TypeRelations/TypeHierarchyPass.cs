namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeHierarchyPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (TypeDeclNode typeDecl in Context.Cpg.Nodes.OfType<TypeDeclNode>())
        {
            if (string.IsNullOrWhiteSpace(typeDecl.BaseTypeName))
            {
                continue;
            }

            TypeDeclNode? baseTypeDecl = FindTypeDecl(typeDecl.BaseTypeName);
            if (baseTypeDecl is not null)
            {
                diff.AddEdge(new CpgEdge(EdgeKinds.InheritsFrom, typeDecl.Id, baseTypeDecl.Id));
            }
        }
    }

    private TypeDeclNode? FindTypeDecl(string typeName)
    {
        return Context.Cpg.Nodes
            .OfType<TypeDeclNode>()
            .FirstOrDefault(
                node =>
                    string.Equals(node.FullName, typeName, StringComparison.Ordinal) ||
                    string.Equals(node.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(node.Name, GetSimpleTypeName(typeName), StringComparison.Ordinal));
    }

    private static string GetSimpleTypeName(string typeName)
    {
        int separatorIndex = typeName.LastIndexOf('.');
        return separatorIndex >= 0 ? typeName[(separatorIndex + 1)..] : typeName;
    }
}
