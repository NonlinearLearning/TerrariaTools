namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeNodePass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        _ = frontendContext;
        HashSet<string> typeNames = new(StringComparer.Ordinal);

        foreach (TypeDeclNode typeDecl in GetNodes<TypeDeclNode>(NodeKinds.TypeDecl))
        {
            string? declaredTypeName = string.IsNullOrWhiteSpace(typeDecl.FullName)
                ? typeDecl.Name
                : typeDecl.FullName;
            if (!string.IsNullOrWhiteSpace(declaredTypeName))
            {
                typeNames.Add(declaredTypeName);
            }

            if (!string.IsNullOrWhiteSpace(typeDecl.BaseTypeName))
            {
                typeNames.Add(typeDecl.BaseTypeName);
            }
        }

        foreach (MethodNode method in GetNodes<MethodNode>(NodeKinds.Method))
        {
            if (!string.IsNullOrWhiteSpace(method.ReturnTypeName))
            {
                typeNames.Add(method.ReturnTypeName);
            }
        }

        foreach (MemberNode member in GetNodes<MemberNode>(NodeKinds.Member))
        {
            if (!string.IsNullOrWhiteSpace(member.TypeFullName))
            {
                typeNames.Add(member.TypeFullName);
            }
        }

        foreach (MethodParameterInNode parameter in GetNodes<MethodParameterInNode>(NodeKinds.MethodParameterIn))
        {
            if (!string.IsNullOrWhiteSpace(parameter.TypeFullName))
            {
                typeNames.Add(parameter.TypeFullName);
            }
        }

        foreach (LocalNode local in GetNodes<LocalNode>(NodeKinds.Local))
        {
            if (!string.IsNullOrWhiteSpace(local.TypeFullName))
            {
                typeNames.Add(local.TypeFullName);
            }
        }

        foreach (CallNode call in GetNodes<CallNode>(NodeKinds.Call))
        {
            if (!string.IsNullOrWhiteSpace(call.TypeFullName))
            {
                typeNames.Add(call.TypeFullName);
            }
        }

        foreach (IdentifierNode identifier in GetNodes<IdentifierNode>(NodeKinds.Identifier))
        {
            if (!string.IsNullOrWhiteSpace(identifier.TypeFullName))
            {
                typeNames.Add(identifier.TypeFullName);
            }
        }

        foreach (FieldIdentifierNode fieldIdentifier in GetNodes<FieldIdentifierNode>(NodeKinds.FieldIdentifier))
        {
            if (!string.IsNullOrWhiteSpace(fieldIdentifier.TypeFullName))
            {
                typeNames.Add(fieldIdentifier.TypeFullName);
            }
        }

        foreach (MethodRefNode methodRef in GetNodes<MethodRefNode>(NodeKinds.MethodRef))
        {
            if (!string.IsNullOrWhiteSpace(methodRef.TypeFullName))
            {
                typeNames.Add(methodRef.TypeFullName);
            }
        }

        foreach (LiteralNode literal in GetNodes<LiteralNode>(NodeKinds.Literal))
        {
            if (!string.IsNullOrWhiteSpace(literal.TypeFullName))
            {
                typeNames.Add(literal.TypeFullName);
            }
        }

        foreach (string typeName in typeNames)
        {
            diff.AddNode(new TypeNode($"type-node:{typeName}", typeName));
        }
    }

    private IReadOnlyList<TNode> GetNodes<TNode>(string nodeKind)
        where TNode : StoredNode
    {
        return Context.Cpg.GetNodesByKind<TNode>(nodeKind);
    }
}
