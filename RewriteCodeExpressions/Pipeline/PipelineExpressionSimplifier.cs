using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TerrariaTools.Analysis;
using TerrariaTools.Diagnostics;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

namespace TerrariaTools.RewriteCodeExpressions.Pipeline
{
    /// <summary>
    /// Hybrid 重写入口：负责协调原始重写逻辑与基于规则的 Hybrid 引擎。
    /// </summary>
    public static class PipelineExpressionSimplifier
    {
        /// <summary>
        /// 异步重写语法树。
        /// </summary>
        /// <param name="root">要重写的根语法节点。</param>
        /// <param name="model">可选的语义模型。如果为 null，将直接返回原始根节点。</param>
        /// <param name="solution">可选的解决方案上下文。</param>
        /// <param name="predicate">用于标识需要标记的节点的谓词。</param>
        /// <param name="nodesToMark">可选的初始标记节点集合。</param>
        /// <param name="globalMethodActions">可选的全局方法操作映射。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="traceContext">可选的重写跟踪上下文。</param>
        /// <param name="useHybrid">是否启用 Hybrid 引擎（目前始终为 true）。</param>
        /// <param name="terrariaConditions">可选的 Terraria 特定重写条件。</param>
        /// <param name="namePattern">用于匹配方法名的正则表达式模式。</param>
        /// <param name="deleteMatched">是否删除匹配的节点。</param>
        /// <param name="clearBodyMatched">是否清除匹配方法的主体内容。</param>
        /// <param name="hybridMetricsSink">用于接收重写指标的回调。</param>
        /// <returns>重写后的语法节点。</returns>
        public static async Task<SyntaxNode> RewriteAsync(
            SyntaxNode root,
            SemanticModel? model,
            Solution? solution,
            System.Func<SyntaxNode, bool> predicate,
            HashSet<SyntaxNode>? nodesToMark = null,
            System.Collections.Generic.Dictionary<IMethodSymbol, FunctionBuildGraph.GraphMethodAction>? globalMethodActions = null,
            System.Threading.CancellationToken cancellationToken = default,
            RewritingTraceContext? traceContext = null,
            bool useHybrid = true,
            IEnumerable<RewriteCondition>? terrariaConditions = null,
            string namePattern = ".*DummyPattern.*",
            bool deleteMatched = true,
            bool clearBodyMatched = false,
            System.Action<HybridRewriteMetrics>? hybridMetricsSink = null)
        {
            _ = traceContext;
            _ = useHybrid;

            if (root == null)
            {
                return null!;
            }

            if (model is null)
            {
                // Hybrid 模式需要语义模型，如果没有则返回原根节点
                return root;
            }

            return await RewriteWithHybridAsync(
                root,
                model,
                solution,
                predicate,
                nodesToMark,
                globalMethodActions,
                terrariaConditions,
                namePattern,
                deleteMatched,
                clearBodyMatched,
                hybridMetricsSink,
                cancellationToken);
        }

        /// <summary>
        /// 同步重写语法树。
        /// </summary>
        /// <param name="root">要重写的根语法节点。</param>
        /// <param name="model">语义模型。</param>
        /// <param name="solution">可选的解决方案上下文。</param>
        /// <param name="predicate">用于标识需要标记的节点的谓词。</param>
        /// <param name="nodesToMark">可选的初始标记节点集合。</param>
        /// <param name="traceContext">可选的重写跟踪上下文。</param>
        /// <param name="useHybrid">是否启用 Hybrid 引擎。</param>
        /// <param name="terrariaConditions">可选的 Terraria 特定重写条件。</param>
        /// <param name="namePattern">用于匹配方法名的正则表达式模式。</param>
        /// <param name="deleteMatched">是否删除匹配的节点。</param>
        /// <param name="clearBodyMatched">是否清除匹配方法的主体内容。</param>
        /// <param name="hybridMetricsSink">用于接收重写指标的回调。</param>
        /// <returns>重写后的语法节点。</returns>
        public static SyntaxNode Rewrite(
            SyntaxNode root,
            SemanticModel? model,
            Solution? solution,
            System.Func<SyntaxNode, bool> predicate,
            HashSet<SyntaxNode>? nodesToMark = null,
            RewritingTraceContext? traceContext = null,
            bool useHybrid = true,
            IEnumerable<RewriteCondition>? terrariaConditions = null,
            string namePattern = ".*DummyPattern.*",
            bool deleteMatched = true,
            bool clearBodyMatched = false,
            System.Action<HybridRewriteMetrics>? hybridMetricsSink = null)
        {
            return Task.Run(async () => await RewriteAsync(
                root,
                model,
                solution,
                predicate,
                nodesToMark,
                null,
                default,
                traceContext,
                useHybrid,
                terrariaConditions,
                namePattern,
                deleteMatched,
                clearBodyMatched,
                hybridMetricsSink)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 使用 Hybrid 引擎异步执行重写。
        /// </summary>
        /// <param name="root">要重写的根节点。</param>
        /// <param name="model">语义模型。</param>
        /// <param name="solution">可选的解决方案。</param>
        /// <param name="predicate">节点标记谓词。</param>
        /// <param name="nodesToMark">初始标记节点。</param>
        /// <param name="globalMethodActions">全局方法操作映射。</param>
        /// <param name="terrariaConditions">Terraria 重写条件。</param>
        /// <param name="namePattern">方法名匹配模式。</param>
        /// <param name="deleteMatched">是否删除匹配项。</param>
        /// <param name="clearBodyMatched">是否清除方法体。</param>
        /// <param name="hybridMetricsSink">指标回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步重写后的节点。</returns>
        private static Task<SyntaxNode> RewriteWithHybridAsync(
            SyntaxNode root,
            SemanticModel model,
            Solution? solution,
            System.Func<SyntaxNode, bool> predicate,
            HashSet<SyntaxNode>? nodesToMark,
            System.Collections.Generic.Dictionary<IMethodSymbol, FunctionBuildGraph.GraphMethodAction>? globalMethodActions,
            IEnumerable<RewriteCondition>? terrariaConditions,
            string namePattern,
            bool deleteMatched,
            bool clearBodyMatched,
            System.Action<HybridRewriteMetrics>? hybridMetricsSink,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ruleEngine = new RuleEngine();
            DefaultHybridRuleRegistry.RegisterCoreRules(ruleEngine);

            var engine = new HybridRewriteEngine(ruleEngine);
            var context = engine.CreateContext(model, root.SyntaxTree);
            var markedNodes = BuildMarkedNodeSet(root, model, predicate, nodesToMark, terrariaConditions);
            context.SetState(HybridInputStateKeys.MarkedNodes, markedNodes);
            if (globalMethodActions is not null)
            {
                context.SetState(HybridInputStateKeys.GlobalMethodActions, globalMethodActions);
            }

            if (solution is not null)
            {
                context.SetState(HybridInputStateKeys.Solution, solution);
            }

            context.SetState(HybridInputStateKeys.NamePattern, namePattern);
            context.SetState(HybridInputStateKeys.DeleteMatched, deleteMatched);
            context.SetState(HybridInputStateKeys.ClearBodyMatched, clearBodyMatched);
            var rewrittenRoot = engine.Rewrite(root, context);

            hybridMetricsSink?.Invoke(new HybridRewriteMetrics(
                context.GetState<int>(HybridMetricsStateKeys.PlanItemCount),
                context.GetState<int>(HybridMetricsStateKeys.ExecutedRuleCount),
                context.GetState<int>(HybridMetricsStateKeys.ReplacedNodeCount),
                context.GetState<int>(HybridMetricsStateKeys.DeletedNodeCount)));

            return Task.FromResult(rewrittenRoot);
        }

        /// <summary>
        /// 构建标记节点集合，包括谓词匹配、Terraria 条件匹配以及标记传播。
        /// </summary>
        /// <param name="root">根节点。</param>
        /// <param name="model">语义模型。</param>
        /// <param name="predicate">谓词。</param>
        /// <param name="nodesToMark">初始标记。</param>
        /// <param name="terrariaConditions">条件列表。</param>
        /// <returns>构建好的标记节点集合。</returns>
        private static HashSet<SyntaxNode> BuildMarkedNodeSet(
            SyntaxNode root,
            SemanticModel model,
            System.Func<SyntaxNode, bool> predicate,
            HashSet<SyntaxNode>? nodesToMark,
            IEnumerable<RewriteCondition>? terrariaConditions)
        {
            var marked = nodesToMark is null ? new HashSet<SyntaxNode>() : new HashSet<SyntaxNode>(nodesToMark);
            if (predicate is not null)
            {
                foreach (var node in root.DescendantNodesAndSelf())
                {
                    if (predicate(node))
                    {
                        marked.Add(node);
                    }
                }
            }

            ApplyTerrariaConditionMarks(root, model, marked, terrariaConditions);
            RunPropagation(root, model, marked);

            return marked;
        }

        /// <summary>
        /// 应用 Terraria 特定的条件标记。
        /// </summary>
        /// <param name="root">根节点。</param>
        /// <param name="model">语义模型。</param>
        /// <param name="marked">已标记集合。</param>
        /// <param name="terrariaConditions">条件列表。</param>
        private static void ApplyTerrariaConditionMarks(
            SyntaxNode root,
            SemanticModel model,
            HashSet<SyntaxNode> marked,
            IEnumerable<RewriteCondition>? terrariaConditions)
        {
            var conditions = terrariaConditions?.ToList() ?? new List<RewriteCondition>
            {
                new() { SymbolName = "netMode", Operator = SyntaxKind.EqualsExpression, Value = "1", IsValueLiteral = true },
                new() { SymbolName = "netMode", Operator = SyntaxKind.NotEqualsExpression, Value = "2", IsValueLiteral = true },
                new() { SymbolName = "myPlayer", Operator = SyntaxKind.EqualsExpression, Value = "whoAmI", IsValueLiteral = false }
            };

            foreach (var binary in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
            {
                var left = RemoveParens(binary.Left);
                var right = RemoveParens(binary.Right);
                var leftSymbol = model.GetSymbolInfo(left).Symbol;
                var rightSymbol = model.GetSymbolInfo(right).Symbol;

                if (conditions.Any(condition => MatchesCondition(binary, left, right, leftSymbol, rightSymbol, condition)))
                {
                    marked.Add(binary);
                }
            }
        }

        /// <summary>
        /// 递归移除表达式外层的括号。
        /// </summary>
        private static ExpressionSyntax RemoveParens(ExpressionSyntax expr)
        {
            while (expr is ParenthesizedExpressionSyntax p)
            {
                expr = p.Expression;
            }

            return expr;
        }

        /// <summary>
        /// 检查二进制表达式是否匹配指定的重写条件。
        /// </summary>
        /// <param name="binary">二进制表达式。</param>
        /// <param name="left">左侧表达式。</param>
        /// <param name="right">右侧表达式。</param>
        /// <param name="leftSymbol">左侧符号。</param>
        /// <param name="rightSymbol">右侧符号。</param>
        /// <param name="condition">重写条件。</param>
        /// <returns>如果匹配则返回 true，否则返回 false。</returns>
        private static bool MatchesCondition(
            BinaryExpressionSyntax binary,
            ExpressionSyntax left,
            ExpressionSyntax right,
            ISymbol? leftSymbol,
            ISymbol? rightSymbol,
            RewriteCondition condition)
        {
            if (!binary.IsKind(condition.Operator))
            {
                return false;
            }

            if (IsSymbolMatch(leftSymbol, condition.SymbolName) && IsValueMatch(right, rightSymbol, condition))
            {
                return true;
            }

            if (condition.Operator == SyntaxKind.EqualsExpression
                || condition.Operator == SyntaxKind.NotEqualsExpression)
            {
                if (IsSymbolMatch(rightSymbol, condition.SymbolName) && IsValueMatch(left, leftSymbol, condition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查符号名称是否匹配目标名称。
        /// </summary>
        /// <param name="symbol">要检查的符号。</param>
        /// <param name="targetName">目标名称。</param>
        /// <returns>如果匹配则返回 true，否则返回 false。</returns>
        private static bool IsSymbolMatch(ISymbol? symbol, string targetName)
        {
            return symbol != null && symbol.Name == targetName;
        }

        /// <summary>
        /// 检查表达式的值是否匹配重写条件中的值。
        /// </summary>
        /// <param name="expr">要检查的表达式。</param>
        /// <param name="symbol">表达式对应的符号。</param>
        /// <param name="condition">重写条件。</param>
        /// <returns>如果匹配则返回 true，否则返回 false。</returns>
        private static bool IsValueMatch(ExpressionSyntax expr, ISymbol? symbol, RewriteCondition condition)
        {
            if (condition.IsValueLiteral)
            {
                return expr is LiteralExpressionSyntax literal && literal.Token.ValueText == condition.Value;
            }

            return symbol != null && symbol.Name == condition.Value;
        }

        /// <summary>
        /// 执行标记传播，直到标记集合不再变化。
        /// </summary>
        /// <param name="root">语法树根节点。</param>
        /// <param name="model">语义模型。</param>
        /// <param name="marked">要传播的标记集合。</param>
        private static void RunPropagation(SyntaxNode root, SemanticModel model, HashSet<SyntaxNode> marked)
        {
            int lastCount;
            do
            {
                lastCount = marked.Count;
                var upwardCollector = new UpwardMarkCollector(marked);
                upwardCollector.Visit(root);

                var semanticPropagator = new PreprocessedSymbolPropagator(model, marked, root);
                semanticPropagator.Propagate();
            } while (marked.Count > lastCount);
        }
    }
}
