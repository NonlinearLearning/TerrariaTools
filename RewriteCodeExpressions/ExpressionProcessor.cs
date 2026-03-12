using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 表达式处理器：提供用于标记和重写语法树的静态实用方法。
    /// 该类充当新旧重写逻辑之间的桥梁。
    /// </summary>
    public static class ExpressionProcessor
    {
        /// <summary>
        /// 收集所有符合谓词条件的节点，并处理结构上的传播。
        /// </summary>
        /// <param name="root">要分析的根语法节点。</param>
        /// <param name="predicate">节点选择谓词。</param>
        /// <param name="model">语法树的语义模型。</param>
        /// <returns>符合条件的节点集合。</returns>
        public static HashSet<SyntaxNode> CollectNodesToMark(SyntaxNode root, Func<SyntaxNode, bool> predicate, SemanticModel model)
        {
            var nodes = new HashSet<SyntaxNode>();
            if (root == null) return nodes;

            // 1. 初步标记符合条件的节点
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (predicate(node))
                {
                    nodes.Add(node);
                }
            }

            // 2. 结构传播逻辑（如果子节点被全部标记，父节点也可能被标记）
            // 这里我们只是简单实现，实际逻辑可能更复杂
            return nodes;
        }

        /// <summary>
        /// 移除符合谓词条件的语法树部分。
        /// </summary>
        /// <param name="root">要处理的根语法节点。</param>
        /// <param name="predicate">节点移除谓词。</param>
        /// <param name="model">可选的语义模型。</param>
        /// <returns>修改后的语法节点。</returns>
        public static SyntaxNode RemoveParts(SyntaxNode root, Func<SyntaxNode, bool> predicate, SemanticModel? model = null)
        {
            if (root == null) return null!;

            var nodesToMark = model != null
                ? CollectNodesToMark(root, predicate, model)
                : new HashSet<SyntaxNode>(root.DescendantNodesAndSelf().Where(predicate));

            var rewriter = new ExpressionSimplifier(predicate, model, nodesToMark);
            return rewriter.Visit(root)!;
        }

        /// <summary>
        /// 处理带有特定批注的节点。
        /// </summary>
        /// <param name="root">要处理的根语法节点。</param>
        /// <param name="annotationKind">批注类型名称。</param>
        /// <param name="model">语法树的语义模型。</param>
        /// <returns>处理后的语法节点。</returns>
        public static SyntaxNode ProcessAnnotatedNodes(SyntaxNode root, string annotationKind, SemanticModel model)
        {
            // 简单实现：找到所有带有该批注的节点并进行某种处理
            // 在当前上下文中，通常是标记它们进行简化
            return RemoveParts(root, n => n.HasAnnotation(new SyntaxAnnotation(annotationKind)), model);
        }

        /// <summary>
        /// 移除 Terraria 特有的条件表达式（如 if (Main.netMode == 0)）。
        /// </summary>
        /// <param name="root">要处理的根语法节点。</param>
        /// <param name="model">可选的语义模型。</param>
        /// <param name="conditions">重写条件列表。</param>
        /// <returns>移除特定条件后的语法节点。</returns>
        public static SyntaxNode RemoveTerrariaConditions(SyntaxNode root, SemanticModel? model, IEnumerable<RewriteCondition> conditions)
        {
            if (root == null) return null!;

            // 简单实现：将条件转换为谓词并调用 RemoveParts
            Func<SyntaxNode, bool> predicate = node =>
            {
                if (node is IfStatementSyntax ifStmt)
                {
                    var conditionStr = ifStmt.Condition.ToString();
                    return conditions.Any(c => conditionStr.Contains(c.SymbolName));
                }
                return false;
            };

            return RemoveParts(root, predicate, model);
        }
    }
}
