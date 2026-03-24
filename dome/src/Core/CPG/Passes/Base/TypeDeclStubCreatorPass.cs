namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeDeclStubCreatorPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        HashSet<string> existingTypeNames = Context.Cpg.Nodes
            .OfType<TypeDeclNode>()
            .Select(node => node.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> existingFullTypeNames = Context.Cpg.Nodes
            .OfType<TypeDeclNode>()
            .Select(node => node.FullName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (TypeDeclNode typeDecl in Context.Cpg.Nodes.OfType<TypeDeclNode>())
        {
            if (string.IsNullOrWhiteSpace(typeDecl.BaseTypeName))
            {
                continue;
            }

            string simpleBaseTypeName = GetSimpleTypeName(typeDecl.BaseTypeName);
            if (existingFullTypeNames.Contains(typeDecl.BaseTypeName) || existingTypeNames.Contains(simpleBaseTypeName))
            {
                continue;
            }

            diff.AddNode(new TypeDeclNode(NodeIdFactory.TypeDecl(typeDecl.BaseTypeName), simpleBaseTypeName, fullName: typeDecl.BaseTypeName));
            existingTypeNames.Add(simpleBaseTypeName);
            existingFullTypeNames.Add(typeDecl.BaseTypeName);
        }
    }

    private static string GetSimpleTypeName(string typeName)
    {
        int separatorIndex = typeName.LastIndexOf('.');
        return separatorIndex >= 0 ? typeName[(separatorIndex + 1)..] : typeName;
    }
}
