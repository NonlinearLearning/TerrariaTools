using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using QuikGraph;

using TerrariaTools.Rules.Dome.Mark.ContextRules;
using TerrariaTools.Rules.Dome.Mark.StaticRules;

namespace TerrariaTools.Rules.Dome.Mark
{
    /// <summary>
    /// 上下文传播引擎。
    /// 基于数据流依赖分析和微规则策略，将已有的标记在语法树中进行上下文传播。
    /// </summary>
    public class RuleEngine
    {
        public string Name => "上下文传播引擎";

        public const string RewriteAnnotationKind = RuleConstants.RewriteAnnotationKind;
        public const string ActionDelete = RuleConstants.ActionDelete;

        public SyntaxNode Apply(SyntaxNode root)
        {
            if (root == null) return null;

            // 1. 构建编译单元和语义模型 (为了 DataFlow 分析)
            var compilation = CSharpCompilation.Create("TempAnalysis")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(CreateMetadataReferences())
                .AddSyntaxTrees(root.SyntaxTree);

            var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);

            // 调试：检查编译错误
            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                // 可以记录或抛出异常，这里我们暂且打印
                foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    Console.WriteLine($"[RuleEngine Compilation Error] {diag}");
                }
            }

            // 2. 构建数据流依赖图
            var analyzer = new DataFlowDependencyAnalyzer(semanticModel);
            analyzer.Analyze(root);
            var graph = analyzer.Graph;

            // 3. 识别初始标记节点
            var markedNodes = new HashSet<DataFlowDependencyNode>();
            var queue = new Queue<DataFlowDependencyNode>();
            var ruleReasons = new Dictionary<SyntaxNode, string>();

            foreach (var node in graph.Nodes)
            {
                if (IsNodeMarked(node.Syntax))
                {
                    markedNodes.Add(node);
                    queue.Enqueue(node);
                    ruleReasons[node.Syntax] = "Initial Marking";
                }
            }

            // 4. BFS 传播 (使用自动化规则引擎)
            var inheritanceAnalyzer = new InheritanceAnalyzer();
            var inheritanceGraph = inheritanceAnalyzer.Build(compilation);

            var context = new SpreadingContext
            {
                Graph = graph,
                SemanticModel = semanticModel,
                InheritanceGraph = inheritanceGraph
            };
            var registry = SpreadingRuleRegistry.Instance;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                // 优化：使用 OutEdges 而不是全量过滤
                foreach (var edge in graph.OutEdges(current))
                {
                    var target = edge.Target;
                    if (markedNodes.Contains(target)) continue;

                    // 1. 节点守卫检查 (NodeGuard) - 无上下文，优先执行
                    var nodeGuards = registry.GetNodeGuardRules(target.Syntax.Kind());
                    bool isBlockedByGuard = false;
                    foreach (var guard in nodeGuards)
                    {
                        // NodeGuard 只需要检查目标节点，edge 和 source 可以传 null 或忽略
                        var guardResult = guard.Propagate(current, target, edge, context);
                        if (guardResult.IsHandled && !guardResult.ShouldPropagate)
                        {
                            isBlockedByGuard = true;
                            break; // 只要有一个 Guard 说 Block，就熔断
                        }
                    }

                    if (isBlockedByGuard) continue;

                    // 2. 边传播检查 (EdgePropagator) - 需上下文
                    bool shouldPropagate = false;
                    string propagationRuleName = null;
                    var edgePropagators = registry.GetEdgePropagatorRules(target.Syntax.Kind());

                    foreach (var rule in edgePropagators)
                    {
                        var propResult = rule.Propagate(current, target, edge, context);
                        if (propResult.ShouldPropagate)
                        {
                            shouldPropagate = true;
                            propagationRuleName = rule.GetType().Name;
                        }

                        if (propResult.IsHandled)
                        {
                            // 规则处理完毕，如果是熔断 (Blocked) 则 shouldPropagate 会保持为 false
                            break;
                        }
                    }

                    if (shouldPropagate)
                    {
                        markedNodes.Add(target);
                        queue.Enqueue(target);
                        ruleReasons[target.Syntax] = propagationRuleName ?? "Unknown Propagation Rule";
                    }
                }
            }

            // 5. 应用标记 (区分 Delete 和 CommentOut)
            var nodesToDelete = new List<SyntaxNode>();
            var nodesToComment = new List<SyntaxNode>();

            // 先识别所有被 BFS 标记的语句
            var initialMarkedStatements = markedNodes
                .Where(n => n.Kind == DataFlowDependencyNodeKind.Statement)
                .Select(n => n.Syntax)
                .ToList();

            // 6. 结构性连坐逻辑 (Try-Catch, Loop etc.)
            var additionalNodesToDelete = new HashSet<SyntaxNode>();

            // 检查 Try 语句
            foreach (var tryStmt in root.DescendantNodes().OfType<TryStatementSyntax>())
            {
                // 如果 try 块内的所有语句都在 initialMarkedStatements 中，则标记整个 try
                var allStatementsMarked = tryStmt.Block.Statements.All(s => initialMarkedStatements.Contains(s));
                if (allStatementsMarked && tryStmt.Block.Statements.Any())
                {
                    additionalNodesToDelete.Add(tryStmt);
                    ruleReasons[tryStmt] = "Structural Cascading (Try)";
                }
            }

            // 检查 For/ForEach 循环
            var loops = root.DescendantNodes().OfType<StatementSyntax>()
                .Where(s => s is ForStatementSyntax || s is CommonForEachStatementSyntax || s is DoStatementSyntax || s is WhileStatementSyntax);

            foreach (var loop in loops)
            {
                StatementSyntax loopStatement = null;
                if (loop is ForStatementSyntax f) loopStatement = f.Statement;
                else if (loop is CommonForEachStatementSyntax fe) loopStatement = fe.Statement;
                else if (loop is DoStatementSyntax d) loopStatement = d.Statement;
                else if (loop is WhileStatementSyntax w) loopStatement = w.Statement;

                var body = loopStatement as BlockSyntax;
                var statements = body?.Statements ?? (loopStatement != null ? new SyntaxList<StatementSyntax>(loopStatement) : default);

                if (statements.Any() && statements.All(s => initialMarkedStatements.Contains(s)))
                {
                    // 循环体为空了，循环本身该死。但要检查循环变量引用
                    if (loop is ForStatementSyntax forLoop)
                    {
                        var loopVars = forLoop.Declaration?.Variables.Select(v => v.Identifier.Text) ?? Enumerable.Empty<string>();
                        // 检查这些变量在循环外部是否有引用
                        bool hasExternalRef = root.DescendantNodes()
                            .OfType<IdentifierNameSyntax>()
                            .Any(id => loopVars.Contains(id.Identifier.Text) && !forLoop.Contains(id));

                        if (!hasExternalRef)
                        {
                            additionalNodesToDelete.Add(forLoop);
                            ruleReasons[forLoop] = "Structural Cascading (Loop)";

                            // 连坐：检查 count 变量 (假设 count 是 limit)
                            var limit = forLoop.Condition?.DescendantNodes().OfType<IdentifierNameSyntax>().FirstOrDefault();
                            if (limit != null)
                            {
                                // 如果 limit 变量在 root 中只有这一次引用，则标记其声明语句
                                var limitSymbol = semanticModel.GetSymbolInfo(limit).Symbol;
                                if (limitSymbol != null)
                                {
                                    var references = root.DescendantNodes().OfType<IdentifierNameSyntax>()
                                        .Where(id => SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(id).Symbol, limitSymbol))
                                        .ToList();

                                    if (references.Count <= 1) // 只有 for 循环里这一次引用
                                    {
                                        var declStmt = limitSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
                                        if (declStmt != null)
                                        {
                                            additionalNodesToDelete.Add(declStmt);
                                            ruleReasons[declStmt] = "Structural Cascading (Dependency Cleanup)";
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        additionalNodesToDelete.Add(loop);
                        ruleReasons[loop] = "Structural Cascading (Loop)";
                    }
                }
            }

            // 合并所有需要处理的节点
            foreach (var node in initialMarkedStatements)
            {
                if (TryMarkObjectInitializerAssignments(node, semanticModel, graph, markedNodes, out var objectInitializerAssignments))
                {
                    foreach (var assignment in objectInitializerAssignments)
                    {
                        ruleReasons[assignment] = "Object Initializer Reset";
                    }
                    continue;
                }

                // 如果父节点（如 try）已经被标记删除，则子节点无需重复标记
                if (additionalNodesToDelete.Any(p => p.Contains(node))) continue;

                if (ShouldCommentOut(node, semanticModel))
                {
                    nodesToComment.Add(node);
                }
                else
                {
                    nodesToDelete.Add(node);
                }
            }

            foreach (var node in additionalNodesToDelete)
            {
                nodesToDelete.Add(node);
            }

            if (nodesToDelete.Count == 0 && nodesToComment.Count == 0 && ruleReasons.Keys.OfType<AssignmentExpressionSyntax>().Any() == false) return root;

            var result = root;
            var objectInitializerResets = ruleReasons.Keys
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.Parent is InitializerExpressionSyntax)
                .ToList();

            if (objectInitializerResets.Count > 0)
            {
                result = result.ReplaceNodes(objectInitializerResets, (original, rewritten) =>
                {
                    var defaultValue = GetObjectInitializerDefaultValue(original, semanticModel);
                    return rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(ObjectInitializerRule.ResetAnnotationKind, defaultValue));
                });
            }

            if (nodesToDelete.Count > 0)
            {
                result = result.ReplaceNodes(nodesToDelete, (original, rewritten) => {
                    var reason = ruleReasons.ContainsKey(original) ? ruleReasons[original] : "Unknown";
                    return rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, $"{RuleConstants.ActionDelete};Reason={reason}"));
                });
            }

            if (nodesToComment.Count > 0)
            {
                result = result.ReplaceNodes(nodesToComment, (original, rewritten) => {
                    var reason = ruleReasons.ContainsKey(original) ? ruleReasons[original] : "Unknown";
                    var annotated = rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, $"{RuleConstants.ActionCommentOut};Reason={reason}"));

                    // 特殊逻辑：如果是字段赋值，携带重置信息
                    if (original is ExpressionStatementSyntax es && es.Expression is AssignmentExpressionSyntax assign)
                    {
                        var symbol = semanticModel.GetSymbolInfo(assign.Left).Symbol;
                        if (symbol?.Kind == SymbolKind.Field || symbol?.Kind == SymbolKind.Property)
                        {
                            var type = (symbol is IFieldSymbol f) ? f.Type : ((IPropertySymbol)symbol).Type;
                            var defaultValue = GetDefaultValue(SyntaxFactory.ParseTypeName(type.ToDisplayString()));

                            // 使用 RuleConstants.ResetAnnotationKind 传递默认值
                            return annotated.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.ResetAnnotationKind, defaultValue));
                        }
                    }
                    return annotated;
                });
            }

            return result;
        }

        private static IEnumerable<MetadataReference> CreateMetadataReferences()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
            {
                foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    paths.Add(path);
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                    {
                        paths.Add(assembly.Location);
                    }
                }
                catch
                {
                    // Ignore assemblies that cannot expose a stable location.
                }
            }

            return paths.Select(path => MetadataReference.CreateFromFile(path));
        }

        private static bool TryMarkObjectInitializerAssignments(
            SyntaxNode node,
            SemanticModel semanticModel,
            DataFlowDependencyGraph graph,
            HashSet<DataFlowDependencyNode> markedNodes,
            out List<AssignmentExpressionSyntax> assignmentsToReset)
        {
            assignmentsToReset = new List<AssignmentExpressionSyntax>();

            if (node is not LocalDeclarationStatementSyntax localDeclaration)
            {
                return false;
            }

            foreach (var assignment in localDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Parent is not InitializerExpressionSyntax)
                {
                    continue;
                }
                assignmentsToReset.Add(assignment);
            }

            return assignmentsToReset.Count > 0;
        }

        private static bool IsNodeMarked(SyntaxNode node)
        {
            return node.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any() ||
                   node.GetAnnotations(RuleConstants.SourceAnnotationKind).Any();
        }

        private static bool HasAction(SyntaxNode node)
        {
            return node.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(a => a.Data != null && a.Data.Contains("Action="));
        }

        private static bool ShouldCommentOut(SyntaxNode node, SemanticModel semanticModel)
        {
            if (node.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(a => a.Data.Contains(RuleConstants.ActionCommentOut)))
            {
                return true;
            }

            if (node is ExpressionStatementSyntax expressionStatement &&
                expressionStatement.Expression is AssignmentExpressionSyntax assignment)
            {
                var symbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                return symbol is IFieldSymbol or IPropertySymbol;
            }

            return false;
        }

        private static string GetDefaultValue(TypeSyntax type)
        {
            if (type == null) return "default";
            if (type is PredefinedTypeSyntax predefined)
            {
                switch (predefined.Keyword.Kind())
                {
                    case SyntaxKind.BoolKeyword: return "false";
                    case SyntaxKind.IntKeyword: return "0";
                    case SyntaxKind.StringKeyword: return "null";
                }
            }
            return "default";
        }

        private static string GetObjectInitializerDefaultValue(AssignmentExpressionSyntax assignment, SemanticModel semanticModel)
        {
            var symbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
            var type = symbol switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (type == null)
            {
                return "default";
            }

            if (type.IsReferenceType || type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                return "null";
            }

            return type.SpecialType switch
            {
                SpecialType.System_Boolean => "false",
                SpecialType.System_Char => "'\\0'",
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Decimal or
                SpecialType.System_Single or
                SpecialType.System_Double => "0",
                _ => "default"
            };
        }
    }
}
