namespace TerrariaTools.Dome.Core.Cpg;

public sealed class AstLinkerPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        NamespaceBlockNode? namespaceBlock = Context.Cpg.Nodes.OfType<NamespaceBlockNode>().FirstOrDefault();
        if (namespaceBlock is null)
        {
            return;
        }

        foreach (TypeDeclNode typeDecl in Context.Cpg.Nodes.OfType<TypeDeclNode>())
        {
            diff.AddEdge(new CpgEdge("AST", namespaceBlock.Id, typeDecl.Id));
        }

        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>().Where(node => node.ContainingTypeName is not null))
        {
            TypeDeclNode? declaringType = Context.Cpg.Nodes
                .OfType<TypeDeclNode>()
                .FirstOrDefault(
                    typeDecl =>
                        string.Equals(typeDecl.FullName, method.ContainingTypeName, StringComparison.Ordinal) ||
                        string.Equals(typeDecl.Name, method.ContainingTypeName, StringComparison.Ordinal));
            if (declaringType is not null)
            {
                diff.AddEdge(new CpgEdge("AST", declaringType.Id, method.Id));
            }
        }

        foreach (BlockNode block in Context.Cpg.Nodes.OfType<BlockNode>().Where(node => node.ContainingTypeName is not null))
        {
            MethodNode? ownerMethod = Context.Cpg.Nodes
                .OfType<MethodNode>()
                .FirstOrDefault(
                    method =>
                        string.Equals(method.Name, block.MethodName, StringComparison.Ordinal) &&
                        string.Equals(method.ContainingTypeName, block.ContainingTypeName, StringComparison.Ordinal));
            if (ownerMethod is not null)
            {
                diff.AddEdge(new CpgEdge("AST", ownerMethod.Id, block.Id));
            }
        }

        foreach (MemberNode member in Context.Cpg.Nodes.OfType<MemberNode>().Where(node => node.ContainingTypeName is not null))
        {
            TypeDeclNode? declaringType = Context.Cpg.Nodes
                .OfType<TypeDeclNode>()
                .FirstOrDefault(
                    typeDecl =>
                        string.Equals(typeDecl.FullName, member.ContainingTypeName, StringComparison.Ordinal) ||
                        string.Equals(typeDecl.Name, member.ContainingTypeName, StringComparison.Ordinal));
            if (declaringType is not null)
            {
                diff.AddEdge(new CpgEdge("AST", declaringType.Id, member.Id));
            }
        }

        foreach (ControlStructureNode controlStructure in Context.Cpg.Nodes.OfType<ControlStructureNode>().Where(node => node.ContainingTypeName is not null))
        {
            BlockNode? ownerBlock = Context.Cpg.Nodes
                .OfType<BlockNode>()
                .FirstOrDefault(
                    block =>
                        string.Equals(block.MethodName, controlStructure.MethodName, StringComparison.Ordinal) &&
                        string.Equals(block.ContainingTypeName, controlStructure.ContainingTypeName, StringComparison.Ordinal));
            if (ownerBlock is not null)
            {
                diff.AddEdge(new CpgEdge("AST", ownerBlock.Id, controlStructure.Id));
            }
        }

        foreach (ReturnNode returnNode in Context.Cpg.Nodes.OfType<ReturnNode>().Where(node => node.ContainingTypeName is not null))
        {
            BlockNode? ownerBlock = Context.Cpg.Nodes
                .OfType<BlockNode>()
                .FirstOrDefault(
                    block =>
                        string.Equals(block.MethodName, returnNode.MethodName, StringComparison.Ordinal) &&
                        string.Equals(block.ContainingTypeName, returnNode.ContainingTypeName, StringComparison.Ordinal));
            if (ownerBlock is not null)
            {
                diff.AddEdge(new CpgEdge("AST", ownerBlock.Id, returnNode.Id));
            }
        }

        foreach (LocalNode local in Context.Cpg.Nodes.OfType<LocalNode>().Where(node => node.ContainingTypeName is not null))
        {
            BlockNode? ownerBlock = Context.Cpg.Nodes
                .OfType<BlockNode>()
                .FirstOrDefault(
                    block =>
                        string.Equals(block.MethodName, local.MethodName, StringComparison.Ordinal) &&
                        string.Equals(block.ContainingTypeName, local.ContainingTypeName, StringComparison.Ordinal));
            if (ownerBlock is not null)
            {
                diff.AddEdge(new CpgEdge("AST", ownerBlock.Id, local.Id));
            }
        }

        foreach (CallNode call in Context.Cpg.Nodes.OfType<CallNode>())
        {
            MethodNode? ownerMethod = Context.Cpg.Nodes
                .OfType<MethodNode>()
                .FirstOrDefault(
                    method =>
                        string.Equals(method.Name, call.OwnerMethodName, StringComparison.Ordinal) &&
                        string.Equals(method.ContainingTypeName, call.ContainingTypeName, StringComparison.Ordinal));
            if (ownerMethod is not null)
            {
                diff.AddEdge(new CpgEdge("AST", ownerMethod.Id, call.Id));
            }
        }
    }
}
