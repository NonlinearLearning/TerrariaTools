using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome.Mark.StaticRules;

namespace TerrariaTools.Rules.Dome.Mark.ContextRules
{
    [SpreadingRule(100, SpreadingRuleType.EdgePropagator,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.IfStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.ForStatement,
        SyntaxKind.ForEachStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.ReturnStatement,
        SyntaxKind.TryStatement,
        SyntaxKind.ThrowStatement,
        SyntaxKind.SwitchStatement)]
    public class DefaultSpreadingRule : ISpreadingRule
    {
        public PropagationResult Propagate(DataFlowDependencyNode source, DataFlowDependencyNode target, DataFlowDependencyEdge edge, SpreadingContext context)
        {
            // 规则 A: 语句被标记 -> 它定义的变量也被视为“污染”
            if (source.Kind == DataFlowDependencyNodeKind.Statement &&
                target.Kind == DataFlowDependencyNodeKind.Variable &&
                edge.Kind == DataFlowDependencyEdgeKind.Defines)
            {
                return PropagationResult.Propagate;
            }

            // 规则 B: 变量被“污染” -> 使用它的语句也被标记
            if (source.Kind == DataFlowDependencyNodeKind.Variable &&
                target.Kind == DataFlowDependencyNodeKind.Statement &&
                edge.Kind == DataFlowDependencyEdgeKind.Uses)
            {
                // 检查污染是否真的能传导到这个语句 (流敏感检查)
                if (IsSanitizedBefore(source, target, context))
                {
                    return PropagationResult.None;
                }

                bool isEscaping = IsEscapingBlock(source, target, context.Graph);
                bool isSameBlock = IsInSameBlock(source.Syntax, target.Syntax);

                if (isEscaping || isSameBlock)
                {
                    return PropagationResult.Propagate;
                }
            }

            return PropagationResult.None;
        }

        private bool IsSanitizedBefore(DataFlowDependencyNode varNode, DataFlowDependencyNode stmtNode, SpreadingContext context)
        {
            // 查找所有定义这个变量的语句 (使用 InEdges 优化)
            var definitions = context.Graph.InEdges(varNode)
                .Where(e => e.Kind == DataFlowDependencyEdgeKind.Defines)
                .Select(e => e.Source)
                .ToList();

            if (definitions.Count <= 1) return false;

            // 查找在 stmtNode 之前的最近定义
            var latestDef = FindLatestDefinition(varNode, stmtNode, context.Graph);
            if (latestDef != null && SanitizationRule.IsSanitizingAssignment(latestDef.Syntax))
            {
                return true;
            }

            return false;
        }

        private DataFlowDependencyNode FindLatestDefinition(DataFlowDependencyNode varNode, DataFlowDependencyNode stmtNode, DataFlowDependencyGraph graph)
        {
            // 沿 Precedes 边缘反向搜索
            var visited = new HashSet<DataFlowDependencyNode>();
            var queue = new Queue<DataFlowDependencyNode>();
            queue.Enqueue(stmtNode);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current)) continue;

                // 查找所有指向当前节点的 Precedes 边缘 (使用 InEdges 优化)
                var predecessors = graph.InEdges(current)
                    .Where(e => e.Kind == DataFlowDependencyEdgeKind.Precedes)
                    .Select(e => e.Source);

                foreach (var pred in predecessors)
                {
                    // 检查前驱节点是否定义了 varNode (使用 OutEdges 优化)
                    if (graph.OutEdges(pred).Any(e => e.Target == varNode && e.Kind == DataFlowDependencyEdgeKind.Defines))
                    {
                        return pred;
                    }
                    queue.Enqueue(pred);
                }
            }

            return null;
        }

        private bool IsInSameBlock(SyntaxNode node1, SyntaxNode node2)
        {
            var block1 = node1.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            var block2 = node2.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            return block1 == block2;
        }

        private bool IsEscapingBlock(DataFlowDependencyNode varNode, DataFlowDependencyNode stmtNode, DataFlowDependencyGraph graph)
        {
            if (varNode.Symbol?.Kind == SymbolKind.Field || varNode.Symbol?.Kind == SymbolKind.Property)
            {
                return true;
            }

            if (varNode.Symbol?.Kind == SymbolKind.Parameter)
            {
                var param = (IParameterSymbol)varNode.Symbol;
                if (param.RefKind == RefKind.Ref || param.RefKind == RefKind.Out)
                {
                    return true;
                }
            }

            var varBlock = varNode.Syntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            var stmtBlock = stmtNode.Syntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault();

            if (varBlock != null && stmtBlock != null && varBlock != stmtBlock)
            {
                var decl = varNode.Symbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                var declBlock = decl?.Ancestors().OfType<BlockSyntax>().FirstOrDefault();

                if (declBlock == stmtBlock || (declBlock != null && stmtBlock.Ancestors().Contains(declBlock)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
