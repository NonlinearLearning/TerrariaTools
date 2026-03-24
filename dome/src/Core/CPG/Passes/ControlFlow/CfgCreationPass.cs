using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class CfgCreationPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        RoslynFrontendContext? frontendContext = Context.Cpg.FrontendContext;
        if (frontendContext is null)
        {
            ApplyLinearFallback(diff);
            return;
        }

        foreach (MethodNode method in Context.Cpg.Nodes.OfType<MethodNode>())
        {
            MethodDeclarationSyntax? methodDeclaration = ControlFlowSyntaxFacts.FindMethodDeclaration(frontendContext, method);
            if (methodDeclaration?.Body is null)
            {
                ApplyLinearFallback(diff, method);
                continue;
            }

            ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds =
                ControlFlowSyntaxFacts.BuildStructuredNodeIds(
                    methodDeclaration,
                    method.ContainingTypeName,
                    method.Name ?? string.Empty);
            IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds =
                ControlFlowSyntaxFacts.BuildInvocationIds(methodDeclaration, method.ContainingTypeName, method.Name ?? string.Empty);

            IReadOnlyList<string> entryNodeIds = BuildBlockEntries(
                methodDeclaration.Body.Statements,
                Array.Empty<string>(),
                invocationIds,
                structuredNodeIds,
                diff);

            foreach (string entryNodeId in entryNodeIds)
            {
                diff.AddEdge(new CpgEdge("CFG", method.Id, entryNodeId));
            }
        }
    }

    private static IReadOnlyList<string> BuildBlockEntries(
        SyntaxList<StatementSyntax> statements,
        IReadOnlyList<string> successorNodeIds,
        IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds,
        ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds,
        DiffGraph diff)
    {
        IReadOnlyList<string> nextSuccessors = successorNodeIds;
        for (int index = statements.Count - 1; index >= 0; index--)
        {
            nextSuccessors = BuildStatementEntries(
                statements[index],
                nextSuccessors,
                invocationIds,
                structuredNodeIds,
                diff);
        }

        return nextSuccessors;
    }

    private static IReadOnlyList<string> BuildStatementEntries(
        StatementSyntax statement,
        IReadOnlyList<string> successorNodeIds,
        IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds,
        ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds,
        DiffGraph diff)
    {
        switch (statement)
        {
            case BlockSyntax block:
                return BuildBlockEntries(block.Statements, successorNodeIds, invocationIds, structuredNodeIds, diff);
            case IfStatementSyntax ifStatement:
                return BuildIfEntries(ifStatement, successorNodeIds, invocationIds, structuredNodeIds, diff);
            case ReturnStatementSyntax returnStatement:
                return BuildReturnEntries(returnStatement, invocationIds, structuredNodeIds, diff);
            default:
                return BuildLinearStatementEntries(statement, successorNodeIds, invocationIds, diff);
        }
    }

    private static IReadOnlyList<string> BuildIfEntries(
        IfStatementSyntax ifStatement,
        IReadOnlyList<string> successorNodeIds,
        IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds,
        ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds,
        DiffGraph diff)
    {
        string controlStructureId = structuredNodeIds.ControlStructureIds[ifStatement];
        IReadOnlyList<string> thenEntryNodeIds = BuildEmbeddedStatementEntries(
            ifStatement.Statement,
            successorNodeIds,
            invocationIds,
            structuredNodeIds,
            diff);
        IReadOnlyList<string> elseEntryNodeIds = ifStatement.Else is null
            ? successorNodeIds
            : BuildEmbeddedStatementEntries(
                ifStatement.Else.Statement,
                successorNodeIds,
                invocationIds,
                structuredNodeIds,
                diff);

        foreach (string targetNodeId in thenEntryNodeIds.Concat(elseEntryNodeIds).Distinct(StringComparer.Ordinal))
        {
            diff.AddEdge(new CpgEdge("CFG", controlStructureId, targetNodeId));
        }

        return [controlStructureId];
    }

    private static IReadOnlyList<string> BuildEmbeddedStatementEntries(
        StatementSyntax statement,
        IReadOnlyList<string> successorNodeIds,
        IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds,
        ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds,
        DiffGraph diff)
    {
        return statement is BlockSyntax block
            ? BuildBlockEntries(block.Statements, successorNodeIds, invocationIds, structuredNodeIds, diff)
            : BuildStatementEntries(statement, successorNodeIds, invocationIds, structuredNodeIds, diff);
    }

    private static IReadOnlyList<string> BuildReturnEntries(
        ReturnStatementSyntax returnStatement,
        IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds,
        ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds,
        DiffGraph diff)
    {
        string returnNodeId = structuredNodeIds.ReturnIds[returnStatement];
        IReadOnlyList<string> entryNodeIds = BuildLinearStatementEntries(returnStatement, [returnNodeId], invocationIds, diff);
        return entryNodeIds.Count == 0 ? [returnNodeId] : entryNodeIds;
    }

    private static IReadOnlyList<string> BuildLinearStatementEntries(
        StatementSyntax statement,
        IReadOnlyList<string> successorNodeIds,
        IReadOnlyDictionary<InvocationExpressionSyntax, string> invocationIds,
        DiffGraph diff)
    {
        string[] callNodeIds = statement.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocationIds[invocation])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (callNodeIds.Length == 0)
        {
            return successorNodeIds;
        }

        for (int index = 0; index < callNodeIds.Length - 1; index++)
        {
            diff.AddEdge(new CpgEdge("CFG", callNodeIds[index], callNodeIds[index + 1]));
        }

        foreach (string successorNodeId in successorNodeIds)
        {
            diff.AddEdge(new CpgEdge("CFG", callNodeIds[^1], successorNodeId));
        }

        return [callNodeIds[0]];
    }

    private void ApplyLinearFallback(DiffGraph diff, MethodNode? onlyMethod = null)
    {
        IEnumerable<MethodNode> methods = onlyMethod is null ? Context.Cpg.Nodes.OfType<MethodNode>() : [onlyMethod];
        foreach (MethodNode method in methods)
        {
            ControlFlowNode[] controlFlowNodes = GetLinearControlFlowNodes(method).ToArray();
            if (controlFlowNodes.Length == 0)
            {
                continue;
            }

            diff.AddEdge(new CpgEdge("CFG", method.Id, controlFlowNodes[0].Id));
            for (int index = 0; index < controlFlowNodes.Length - 1; index++)
            {
                diff.AddEdge(new CpgEdge("CFG", controlFlowNodes[index].Id, controlFlowNodes[index + 1].Id));
            }
        }
    }

    private IEnumerable<ControlFlowNode> GetLinearControlFlowNodes(MethodNode method)
    {
        IEnumerable<ControlFlowNode> controlStructureNodes = Context.Cpg.Nodes
            .OfType<ControlStructureNode>()
            .Where(
                node =>
                    string.Equals(node.MethodName, method.Name, StringComparison.Ordinal) &&
                    string.Equals(node.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
            .Select(node => new ControlFlowNode(node.Id, node.Order));

        IEnumerable<ControlFlowNode> callNodes = Context.Cpg.Nodes
            .OfType<CallNode>()
            .Where(
                call =>
                    string.Equals(call.OwnerMethodName, method.Name, StringComparison.Ordinal) &&
                    string.Equals(call.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
            .Select(call => new ControlFlowNode(call.Id, call.Order ?? int.MaxValue));

        IEnumerable<ControlFlowNode> returnNodes = Context.Cpg.Nodes
            .OfType<ReturnNode>()
            .Where(
                returnNode =>
                    string.Equals(returnNode.MethodName, method.Name, StringComparison.Ordinal) &&
                    string.Equals(returnNode.ContainingTypeName, method.ContainingTypeName, StringComparison.Ordinal))
            .Select(returnNode => new ControlFlowNode(returnNode.Id, returnNode.Order));

        return controlStructureNodes
            .Concat(callNodes)
            .Concat(returnNodes)
            .OrderBy(node => node.Order)
            .ThenBy(node => node.Id, StringComparer.Ordinal);
    }

    private sealed record ControlFlowNode(string Id, int Order);
}
