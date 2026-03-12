using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

/// <summary>
/// 执行 Pass 2：基于 RewritePlan 执行中间件管道并回写语法树。
/// </summary>
public sealed class RewriteExecutionPass
{
    public (SyntaxNode Root, ExecutionSummary Summary) Execute(SyntaxNode root, RewritePlan plan, IRewriteContext context)
    {
        if (plan.Items.Count == 0)
        {
            return (root, new ExecutionSummary(0, 0, 0));
        }

        int executedRuleCount = 0;
        int replacedNodeCount = 0;
        int deletedNodeCount = 0;

        var orderedItems = plan.Items
            .OrderByDescending(item => item.Node.SpanStart)
            .ThenByDescending(item => item.Node.Span.Length)
            .ToList();

        var trackedRoot = root.TrackNodes(orderedItems.Select(item => item.Node));
        foreach (var item in orderedItems)
        {
            var currentNode = trackedRoot.GetCurrentNode(item.Node);
            if (currentNode is null)
            {
                continue;
            }

            executedRuleCount++;
            var rewrittenNode = ExecuteRule(currentNode, item.Rule, context);
            if (TryApplyStatementInsertions(context, trackedRoot, currentNode, rewrittenNode, out var insertionRoot))
            {
                trackedRoot = insertionRoot!;
                if (rewrittenNode.HasAnnotation(ExecutionAnnotations.DeleteNode))
                {
                    deletedNodeCount++;
                }
                else if (rewrittenNode != currentNode)
                {
                    replacedNodeCount++;
                }
                continue;
            }

            if (rewrittenNode.HasAnnotation(ExecutionAnnotations.DeleteNode))
            {
                var removed = trackedRoot.RemoveNode(currentNode, SyntaxRemoveOptions.KeepNoTrivia);
                if (removed is not null)
                {
                    trackedRoot = removed;
                    deletedNodeCount++;
                }
                continue;
            }

            if (rewrittenNode == currentNode)
            {
                continue;
            }

            trackedRoot = trackedRoot.ReplaceNode(currentNode, rewrittenNode);
            replacedNodeCount++;
        }

        var summary = new ExecutionSummary(executedRuleCount, replacedNodeCount, deletedNodeCount);
        return (trackedRoot, summary);
    }

    private static SyntaxNode ExecuteRule(SyntaxNode node, IRule rule, IRewriteContext context)
    {
        var executeMethod = typeof(RewriteExecutionPass)
            .GetMethod(nameof(ExecuteRuleTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(rule.NodeType);

        var result = executeMethod.Invoke(obj: null, parameters: [node, rule, context]);
        return result as SyntaxNode ?? node;
    }

    private static SyntaxNode ExecuteRuleTyped<TNode>(SyntaxNode node, IRule rule, IRewriteContext context)
        where TNode : SyntaxNode
    {
        if (node is not TNode typedNode)
        {
            return node;
        }

        var middlewareTypes = rule.GetMiddlewareTypes().ToList();
        if (middlewareTypes.Count == 0)
        {
            return node;
        }

        var middlewares = middlewareTypes
            .Select(MiddlewareFactory.Create<TNode>)
            .ToList();

        var pipeline = new MiddlewarePipeline<TNode>(middlewares);
        return pipeline.Execute(typedNode, context);
    }

    private static bool TryApplyStatementInsertions(
        IRewriteContext context,
        SyntaxNode trackedRoot,
        SyntaxNode currentNode,
        SyntaxNode rewrittenNode,
        out SyntaxNode? resultRoot)
    {
        resultRoot = null;
        if (currentNode is not StatementSyntax currentStatement || currentStatement.Parent is not BlockSyntax parentBlock)
        {
            return false;
        }

        var beforeStatements = ResolveInsertionStatements(context, rewrittenNode, ExecutionAnnotations.InsertBeforeStatements.Kind, AtomicOperationStateKeys.InsertBeforeRegistry);
        var afterStatements = ResolveInsertionStatements(context, rewrittenNode, ExecutionAnnotations.InsertAfterStatements.Kind, AtomicOperationStateKeys.InsertAfterRegistry);
        if (beforeStatements.Count == 0 && afterStatements.Count == 0)
        {
            return false;
        }

        var replacementStatements = new List<StatementSyntax>(beforeStatements.Count + afterStatements.Count + 1);
        replacementStatements.AddRange(beforeStatements);

        if (!rewrittenNode.HasAnnotation(ExecutionAnnotations.DeleteNode))
        {
            if (rewrittenNode is not StatementSyntax rewrittenStatement)
            {
                return false;
            }

            replacementStatements.Add(rewrittenStatement);
        }

        replacementStatements.AddRange(afterStatements);

        var statementIndex = parentBlock.Statements.IndexOf(currentStatement);
        if (statementIndex < 0)
        {
            return false;
        }

        var updatedStatements = parentBlock.Statements
            .Take(statementIndex)
            .Concat(replacementStatements)
            .Concat(parentBlock.Statements.Skip(statementIndex + 1));

        var newBlock = parentBlock.WithStatements(SyntaxFactory.List(updatedStatements));
        resultRoot = trackedRoot.ReplaceNode(parentBlock, newBlock);
        return true;
    }

    private static List<StatementSyntax> ResolveInsertionStatements(
        IRewriteContext context,
        SyntaxNode rewrittenNode,
        string annotationKind,
        string stateRegistryKey)
    {
        var annotation = rewrittenNode.GetAnnotations(annotationKind).FirstOrDefault();
        if (annotation is null || string.IsNullOrWhiteSpace(annotation.Data))
        {
            return new List<StatementSyntax>();
        }

        var registry = context.GetState<Dictionary<string, List<StatementSyntax>>>(stateRegistryKey);
        if (registry is null || !registry.TryGetValue(annotation.Data, out var statements))
        {
            return new List<StatementSyntax>();
        }

        return statements.Select(s => s.WithoutTrivia()).ToList();
    }
}
