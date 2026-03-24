using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public static class ControlFlowSyntaxFacts
{
    public sealed record StructuredNodeIds(
        IReadOnlyDictionary<IfStatementSyntax, string> ControlStructureIds,
        IReadOnlyDictionary<ReturnStatementSyntax, string> ReturnIds);

    public static MethodDeclarationSyntax? FindMethodDeclaration(
        RoslynFrontendContext frontendContext,
        MethodNode method)
    {
        return frontendContext.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(
                declaration =>
                {
                    IMethodSymbol? symbol = frontendContext.SemanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
                    return string.Equals(RoslynSymbolNameFormatter.GetFullName(symbol), method.FullName, StringComparison.Ordinal);
                });
    }

    public static IReadOnlyDictionary<InvocationExpressionSyntax, string> BuildInvocationIds(
        MethodDeclarationSyntax methodDeclaration,
        string? containingTypeName,
        string ownerMethodName)
    {
        return methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(
                (invocation, index) =>
                {
                    string? targetMethodName = invocation.Expression.ToString().Split('.').LastOrDefault();
                    return new
                    {
                        Invocation = invocation,
                        NodeId = NodeIdFactory.Call(containingTypeName, ownerMethodName, targetMethodName, index),
                    };
                })
            .ToDictionary(
                item => item.Invocation,
                item => item.NodeId,
                ReferenceEqualityComparer.Instance as IEqualityComparer<InvocationExpressionSyntax>);
    }

    public static StructuredNodeIds BuildStructuredNodeIds(
        MethodDeclarationSyntax methodDeclaration,
        string? containingTypeName,
        string methodName)
    {
        Dictionary<IfStatementSyntax, string> controlStructureIds = new(ReferenceEqualityComparer.Instance);
        Dictionary<ReturnStatementSyntax, string> returnIds = new(ReferenceEqualityComparer.Instance);
        int order = 0;

        if (methodDeclaration.Body is not null)
        {
            foreach (StatementSyntax statement in methodDeclaration.Body.Statements)
            {
                IndexStructuredStatements(statement, containingTypeName, methodName, ref order, controlStructureIds, returnIds);
            }
        }

        return new StructuredNodeIds(controlStructureIds, returnIds);
    }

    private static void IndexStructuredStatements(
        StatementSyntax statement,
        string? containingTypeName,
        string methodName,
        ref int order,
        IDictionary<IfStatementSyntax, string> controlStructureIds,
        IDictionary<ReturnStatementSyntax, string> returnIds)
    {
        switch (statement)
        {
            case IfStatementSyntax ifStatement:
                controlStructureIds[ifStatement] = NodeIdFactory.ControlStructure(containingTypeName, methodName, order);
                order++;
                IndexEmbeddedStatement(ifStatement.Statement, containingTypeName, methodName, ref order, controlStructureIds, returnIds);
                if (ifStatement.Else is not null)
                {
                    IndexEmbeddedStatement(ifStatement.Else.Statement, containingTypeName, methodName, ref order, controlStructureIds, returnIds);
                }

                break;
            case ReturnStatementSyntax returnStatement:
                returnIds[returnStatement] = NodeIdFactory.Return(containingTypeName, methodName, order);
                order++;
                break;
            case BlockSyntax block:
                foreach (StatementSyntax nestedStatement in block.Statements)
                {
                    IndexStructuredStatements(nestedStatement, containingTypeName, methodName, ref order, controlStructureIds, returnIds);
                }

                break;
        }
    }

    private static void IndexEmbeddedStatement(
        StatementSyntax statement,
        string? containingTypeName,
        string methodName,
        ref int order,
        IDictionary<IfStatementSyntax, string> controlStructureIds,
        IDictionary<ReturnStatementSyntax, string> returnIds)
    {
        if (statement is BlockSyntax block)
        {
            foreach (StatementSyntax nestedStatement in block.Statements)
            {
                IndexStructuredStatements(nestedStatement, containingTypeName, methodName, ref order, controlStructureIds, returnIds);
            }

            return;
        }

        IndexStructuredStatements(statement, containingTypeName, methodName, ref order, controlStructureIds, returnIds);
    }
}
