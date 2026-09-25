using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Diagnostics;

namespace NLCPG.Builder.Passes
{
    internal sealed class SyntaxPass : INLCPGPass
    {
        internal static SyntaxPass Instance { get; } = new();

        private SyntaxPass()
        {
        }

        public string Name => nameof(SyntaxPass);

        // 触发语法 pass，物化语法节点、token 与语义边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            // Pass 入口只负责把执行委托给 builder，具体构图细节统一留在构建器内部。
            builder.RunSyntaxPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        private readonly record struct SyntaxTraversalFrame(
          SyntaxNode Syntax,
          NLCPGNode Parent,
          NLCPGNode? Current,
          bool EmitTokens);

        private readonly record struct SyntaxTypeResolution(
          ITypeSymbol? TypeSymbol,
          bool QueriedSemanticModel,
          bool ReusedReferencedSymbolType);

        /// <summary>
        /// <c>SyntaxPass</c> 最近一次运行的**专用遥测**。
        /// <para>
        /// 存在的理由：<c>SyntaxPassMetrics</c> 是私有嵌套类且只被局部变量持有，
        /// 构建结束后完全不可达，于是"声明符号查询只对可声明节点发起"这一语义
        /// <b>没有任何可观测面</b>——既无法在测试中断言，也无法做出前后的计数对比。
        /// 本记录把该计数提升为可在构建后读取的事实。
        /// </para>
        /// </summary>
        /// <param name="DeclaredSymbolQueryCount">
        /// 实际发起 <c>GetDeclaredSymbol</c> 的语法节点数。
        /// <b>不等于</b> <paramref name="SyntaxNodeCount"/>：只有
        /// <c>CanDeclareSymbol</c> 认可的节点才计数，这正是本优化要护住的性质。
        /// </param>
        /// <param name="Partitioned">
        /// 本次运行走的是分区语法路径（<see langword="true"/>）还是 legacy 路径。
        /// 两条路径各自创建自己的 metrics，靠该字段区分来源。
        /// </param>
        internal readonly record struct SyntaxPassTelemetry(
          int DeclaredSymbolQueryCount,
          int DeclaredSymbolResolvedCount,
          int SyntaxNodeCount,
          int SyntaxTokenCount,
          long TraversalElapsedMilliseconds,
          bool Partitioned)
        {
            /// <summary>可声明节点中被查询、但未解析出符号的数量。</summary>
            internal int UnresolvedDeclarationCount =>
              DeclaredSymbolQueryCount - DeclaredSymbolResolvedCount;

            /// <summary>
            /// 被"不可声明"判定挡下的语法节点数——即本优化省掉的查询量。
            /// 非负是本优化的核心不变量。
            /// </summary>
            internal int SkippedDeclaredSymbolQueryCount =>
              SyntaxNodeCount - DeclaredSymbolQueryCount;
        }

        /// <summary>
        /// <c>SyntaxPass</c> 最近一次运行的遥测；默认值表示该 pass 尚未运行。
        /// 声明在 <c>SyntaxPass.cs</c>（它持有 <c>NLCPG.Builder</c> partial）内，
        /// 避免触碰并发会话正在重写的 <c>NLCPGBuilder.cs</c>。
        /// </summary>
        internal SyntaxPassTelemetry LastSyntaxPassTelemetry { get; private set; }

        private sealed class SyntaxPassMetrics
        {
            public long TraversalElapsedMilliseconds { get; set; }
            public long CreateSyntaxNodeElapsedMilliseconds { get; set; }
            public long EmitChildTokensElapsedMilliseconds { get; set; }
            public long AddDeclaredSymbolEdgesElapsedMilliseconds { get; set; }
            public long AddReferencedSymbolEdgesElapsedMilliseconds { get; set; }
            public long AddTypeInfoElapsedMilliseconds { get; set; }
            public long ResolveTypeInfoElapsedMilliseconds { get; set; }
            public long AddSyntaxTypeEdgesElapsedMilliseconds { get; set; }
            public long AddTypeReferenceEdgesElapsedMilliseconds { get; set; }
            public int TypeInfoQueryCount { get; set; }
            public int TypeInfoResolvedCount { get; set; }
            public int TypeInfoSymbolReuseCount { get; set; }
            public int DeclaredSymbolQueryCount { get; set; }
            public int DeclaredSymbolResolvedCount { get; set; }
            public int OperationBackedTypeInfoDeferredCount { get; set; }
            public int SyntaxNodeCount { get; set; }
            public int SyntaxTokenCount { get; set; }
        }

        private void RunLegacySyntaxPass(NLCPGBuildContext context, CapabilityBuildPlan buildPlan)
        {
            var metrics = new SyntaxPassMetrics();
            // 整棵语法树沿用显式栈遍历，保证节点与 token 的物化顺序稳定。
            VisitSyntaxIterative(
              context.Root,
              context.SyntaxTreeNode,
              context.Graph,
              context.SemanticModel,
              context.FilePath,
              metrics,
              buildPlan);
            PublishSyntaxPassTelemetry(metrics, partitioned: false);
        }

        /// <summary>
        /// 把某条语法路径的局部 metrics 提升为构建后可读的遥测。
        /// 在两条路径各自的出口各调用一次——它们是互斥的，所以谁最后跑谁生效。
        /// </summary>
        private void PublishSyntaxPassTelemetry(SyntaxPassMetrics metrics, bool partitioned)
        {
            LastSyntaxPassTelemetry = new SyntaxPassTelemetry(
              DeclaredSymbolQueryCount: metrics.DeclaredSymbolQueryCount,
              DeclaredSymbolResolvedCount: metrics.DeclaredSymbolResolvedCount,
              SyntaxNodeCount: metrics.SyntaxNodeCount,
              SyntaxTokenCount: metrics.SyntaxTokenCount,
              TraversalElapsedMilliseconds: metrics.TraversalElapsedMilliseconds,
              Partitioned: partitioned);
        }

        internal void RunSyntaxPass(NLCPGBuildContext context)
        {
            RunLegacySyntaxPass(context, ResolveCapabilityBuildPlan());
        }

        private void RunPartitionedSyntaxPass(
          NLCPGBuildContext context,
          IReadOnlyList<SyntaxNode> partitionRoots,
          IReadOnlyList<SyntaxNode[]> partitions,
          CapabilityBuildPlan buildPlan)
        {
            var metrics = new SyntaxPassMetrics();
            // 分区外语法仍由当前线程串行落图，保证父子链和全局顺序先稳定下来。
            VisitSyntaxOutsidePartitions(
              context.Root,
              context.SyntaxTreeNode,
              context.Graph,
              context.SemanticModel,
              context.FilePath,
              partitionRoots,
              metrics,
              buildPlan);

            for (var partitionIndex = 0; partitionIndex < partitions.Count; partitionIndex++)
            {
                var partition = partitions[partitionIndex];
                var beforeNodeCount = context.Graph.Nodes.Count;
                var materializationStopwatch = PartitionPerformanceDiagnosticsEnabled
                  ? Stopwatch.StartNew()
                  : null;
                // 分区内节点的语义事实已预采集，这里只做按顺序物化和 token 补全。
                try
                {
                    foreach (var syntax in partition)
                    {
                        var parent = syntax.Parent is null
                          ? context.SyntaxTreeNode
                          : _syntaxNodes[syntax.Parent];
                        var syntaxNode = CreateSyntaxNode(
                          syntax,
                          parent,
                          context.Graph,
                          context.SemanticModel,
                          context.FilePath,
                          metrics,
                          buildPlan);
                        EmitChildTokens(syntax, syntaxNode, context.Graph, context.FilePath, metrics, buildPlan);
                    }
                }
                finally
                {
                    materializationStopwatch?.Stop();
                    if (PartitionPerformanceDiagnosticsEnabled)
                    {
                        var spanStart = partition.Length == 0 ? 0 : partition[0].SpanStart;
                        var spanEnd = partition.Length == 0 ? 0 : partition[^1].Span.End;
                        RecordPartitionPerformanceEvent(
                          PartitionPerformanceStageId.SyntaxMaterialization,
                          CreatePartitionPerformanceId("syntax", partitionIndex, spanStart, spanEnd),
                          partitionIndex,
                          partition.Length,
                          Math.Max(0, context.Graph.Nodes.Count - beforeNodeCount),
                          materializationStopwatch?.ElapsedMilliseconds ?? 0,
                          materializationStopwatch?.ElapsedMilliseconds ?? 0);
                    }
                }
            }

            PublishSyntaxPassTelemetry(metrics, partitioned: true);
        }

        private void VisitSyntaxOutsidePartitions(
          SyntaxNode syntax,
          NLCPGNode parent,
          NLCPGGraph graph,
          SemanticModel semanticModel,
          string filePath,
          IReadOnlyCollection<SyntaxNode> partitionRoots,
          SyntaxPassMetrics metrics,
          CapabilityBuildPlan buildPlan)
        {
            if (partitionRoots.Contains(syntax))
            {
                return;
            }

            var syntaxNode = CreateSyntaxNode(syntax, parent, graph, semanticModel, filePath, metrics, buildPlan);
            foreach (var child in syntax.ChildNodes())
            {
                // 只有分区外节点会继续向下递归，避免和分区提交阶段重复建图。
                VisitSyntaxOutsidePartitions(
                  child,
                  syntaxNode,
                  graph,
                  semanticModel,
                  filePath,
                  partitionRoots,
                  metrics,
                  buildPlan);
            }

            EmitChildTokens(syntax, syntaxNode, graph, filePath, metrics, buildPlan);
        }

        private void VisitSyntaxIterative(
          SyntaxNode syntax,
          NLCPGNode parent,
          NLCPGGraph graph,
          SemanticModel semanticModel,
          string filePath,
          SyntaxPassMetrics metrics,
          CapabilityBuildPlan buildPlan)
        {
            var traversalStopwatch = Stopwatch.StartNew();
            var pending = new Stack<SyntaxTraversalFrame>();
            pending.Push(new SyntaxTraversalFrame(syntax, parent, Current: null, EmitTokens: false));

            while (pending.Count > 0)
            {
                var frame = pending.Pop();
                if (frame.EmitTokens)
                {
                    // 第二次出栈只补 token，保持“先节点后 token”的稳定结构。
                    EmitChildTokens(frame.Syntax, frame.Current!.Value, graph, filePath, metrics, buildPlan);
                    continue;
                }

                var syntaxNode = CreateSyntaxNode(
                  frame.Syntax,
                  frame.Parent,
                  graph,
                  semanticModel,
                  filePath,
                  metrics,
                  buildPlan);
                pending.Push(new SyntaxTraversalFrame(frame.Syntax, frame.Parent, syntaxNode, EmitTokens: true));

                // 倒序压栈，弹出时才能恢复 Roslyn 原始的子节点顺序。
                var childNodes = frame.Syntax.ChildNodes().ToArray();
                for (var index = childNodes.Length - 1; index >= 0; index -= 1)
                {
                    pending.Push(new SyntaxTraversalFrame(childNodes[index], syntaxNode, Current: null, EmitTokens: false));
                }
            }

            traversalStopwatch.Stop();
            metrics.TraversalElapsedMilliseconds = traversalStopwatch.ElapsedMilliseconds;
        }

        private NLCPGNode CreateSyntaxNode(
          SyntaxNode syntax,
          NLCPGNode parent,
          NLCPGGraph graph,
          SemanticModel semanticModel,
          string filePath,
          SyntaxPassMetrics metrics,
          CapabilityBuildPlan buildPlan)
        {
            var createNodeStopwatch = Stopwatch.StartNew();
            var syntaxNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: NLCPGNodeKind.SyntaxNode,
              Name: syntax switch
              {
                  BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
                  BaseMethodDeclarationSyntax methodDeclaration => NameOfMethod(methodDeclaration),
                  VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
                  ParameterSyntax parameter => parameter.Identifier.ValueText,
                  _ => null,
              },
              FilePath: filePath,
              SpanStart: syntax.SpanStart,
              SpanEnd: syntax.Span.End,
              StableIdentityText: syntax.Kind().ToString()));
            createNodeStopwatch.Stop();
            metrics.CreateSyntaxNodeElapsedMilliseconds += createNodeStopwatch.ElapsedMilliseconds;
            metrics.SyntaxNodeCount += 1;
            _syntaxNodes[syntax] = syntaxNode;
            graph.AddEdge(parent, syntaxNode, NLCPGEdgeKind.SyntaxChild);

            // 分区版优先复用已缓存的语义事实，避免提交阶段重复查询 SemanticModel。
            _partitionedSyntaxFacts.TryGetValue(syntax, out var cachedFacts);

            // 声明符号只对可声明节点查询，避免无意义的语义调用污染热点统计。
            var declaredSymbolStopwatch = Stopwatch.StartNew();
            var queriedDeclaredSymbol = cachedFacts?.QueriedDeclaredSymbol ?? false;
            var declaredSymbol = cachedFacts?.DeclaredSymbol;
            if (cachedFacts is null && CanDeclareSymbol(syntax))
            {
                declaredSymbol = semanticModel.GetDeclaredSymbol(syntax);
                queriedDeclaredSymbol = true;
            }

            AddDeclaredSymbolEdges(syntaxNode, declaredSymbol, graph);
            declaredSymbolStopwatch.Stop();
            metrics.AddDeclaredSymbolEdgesElapsedMilliseconds += declaredSymbolStopwatch.ElapsedMilliseconds;
            metrics.DeclaredSymbolQueryCount += queriedDeclaredSymbol ? 1 : 0;
            metrics.DeclaredSymbolResolvedCount += declaredSymbol is null ? 0 : 1;

            ISymbol? referencedSymbol = null;
            if (buildPlan.EmitReferences)
            {
                // 引用符号允许直接复用分区预分析结果，否则按节点种类按需查询。
                var referencedSymbolStopwatch = Stopwatch.StartNew();
                referencedSymbol = cachedFacts is null
                  ? (CanReferenceSymbol(syntax) ? semanticModel.GetSymbolInfo(syntax).Symbol : null)
                  : cachedFacts.ReferencedSymbol;
                AddReferencedSymbolEdges(syntax, syntaxNode, referencedSymbol, graph);
                referencedSymbolStopwatch.Stop();
                metrics.AddReferencedSymbolEdgesElapsedMilliseconds += referencedSymbolStopwatch.ElapsedMilliseconds;
            }

            // 能交给 OperationPass 提供类型的表达式先登记，当前阶段避免重复算类型。
            var shouldDeferToOperation = cachedFacts?.ShouldDeferToOperation ?? ShouldDeferSyntaxTypeToOperation(syntax);
            if (shouldDeferToOperation)
            {
                _pendingOperationSyntaxTypeNodes.Add(syntax);
                metrics.OperationBackedTypeInfoDeferredCount += 1;
            }

            // 类型解析优先复用缓存或引用符号类型，只有必要时才回落到 SemanticModel.GetTypeInfo。
            var resolveTypeInfoStopwatch = Stopwatch.StartNew();
            var typeResolution = cachedFacts?.TypeResolution ?? (shouldDeferToOperation
              ? new SyntaxTypeResolution(null, QueriedSemanticModel: false, ReusedReferencedSymbolType: false)
              : ResolveSyntaxTypeSymbol(syntax, semanticModel, referencedSymbol));
            resolveTypeInfoStopwatch.Stop();
            metrics.ResolveTypeInfoElapsedMilliseconds += resolveTypeInfoStopwatch.ElapsedMilliseconds;
            metrics.TypeInfoQueryCount += typeResolution.QueriedSemanticModel ? 1 : 0;
            metrics.TypeInfoResolvedCount += typeResolution.TypeSymbol is null ? 0 : 1;
            metrics.TypeInfoSymbolReuseCount += typeResolution.ReusedReferencedSymbolType ? 1 : 0;

            // 解析出类型后立即补 Syntax HasType 边，确保后续 pass 看到的是完整节点形状。
            var addTypeEdgesStopwatch = Stopwatch.StartNew();
            AddTypeEdges(syntaxNode, typeResolution.TypeSymbol, graph);
            addTypeEdgesStopwatch.Stop();
            metrics.AddSyntaxTypeEdgesElapsedMilliseconds += addTypeEdgesStopwatch.ElapsedMilliseconds;
            metrics.AddTypeInfoElapsedMilliseconds +=
              resolveTypeInfoStopwatch.ElapsedMilliseconds + addTypeEdgesStopwatch.ElapsedMilliseconds;

            if (buildPlan.EmitTypeReferences)
            {
                // TypeRef 边单独记时，因为它既依赖语法形状，也依赖上一步的类型结果。
                var typeReferenceStopwatch = Stopwatch.StartNew();
                AddTypeReferenceEdges(syntax, syntaxNode, graph, semanticModel, typeResolution.TypeSymbol);
                typeReferenceStopwatch.Stop();
                metrics.AddTypeReferenceEdgesElapsedMilliseconds += typeReferenceStopwatch.ElapsedMilliseconds;
            }
            return syntaxNode;
        }

        private SyntaxTypeResolution ResolveSyntaxTypeSymbol(SyntaxNode syntax, SemanticModel semanticModel, ISymbol? referencedSymbol)
        {
            if (syntax is not ExpressionSyntax and not TypeSyntax and not BaseTypeSyntax)
            {
                return new SyntaxTypeResolution(null, QueriedSemanticModel: false, ReusedReferencedSymbolType: false);
            }

            if (_options.EnableReferencedSymbolTypeReuse &&
                TryResolveTypeFromReferencedSymbol(referencedSymbol, out var reusedTypeSymbol))
            {
                return new SyntaxTypeResolution(
                  reusedTypeSymbol,
                  QueriedSemanticModel: false,
                ReusedReferencedSymbolType: true);
            }

            // BaseTypeSyntax 需要落到其内部 Type 节点取类型，其余节点直接按当前语法查询。
            var typeSymbol = syntax switch
            {
                BaseTypeSyntax baseType => semanticModel.GetTypeInfo(baseType.Type).Type,
                _ => semanticModel.GetTypeInfo(syntax).Type,
            };
            return new SyntaxTypeResolution(typeSymbol, QueriedSemanticModel: true, ReusedReferencedSymbolType: false);
        }

        private static bool TryResolveTypeFromReferencedSymbol(ISymbol? symbol, out ITypeSymbol? typeSymbol)
        {
            typeSymbol = symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IEventSymbol eventSymbol => eventSymbol.Type,
                ITypeSymbol type => type,
                _ => null,
            };
            if (typeSymbol is null || typeSymbol.TypeKind is TypeKind.Dynamic or TypeKind.Error)
            {
                typeSymbol = null;
                return false;
            }

            return true;
        }

        private bool ShouldDeferSyntaxTypeToOperation(SyntaxNode syntax)
        {
            if (!_options.EnableOperationBackedSyntaxTypes || syntax is not ExpressionSyntax)
            {
                return false;
            }

            return syntax is IdentifierNameSyntax or
              MemberAccessExpressionSyntax or
              BinaryExpressionSyntax or
              ConditionalExpressionSyntax or
              InvocationExpressionSyntax or
              ElementAccessExpressionSyntax or
              ParenthesizedExpressionSyntax or
              CastExpressionSyntax or
              PrefixUnaryExpressionSyntax or
              PostfixUnaryExpressionSyntax;
        }

        private static void EmitChildTokens(
          SyntaxNode syntax,
          NLCPGNode syntaxNode,
          NLCPGGraph graph,
          string filePath,
          SyntaxPassMetrics metrics,
          CapabilityBuildPlan buildPlan)
        {
            if (!buildPlan.EmitSyntaxTokens)
            {
                return;
            }

            var tokenStopwatch = Stopwatch.StartNew();
            var tokenCount = 0;
            foreach (var childToken in syntax.ChildTokens())
            {
                var tokenNode = graph.AddNode(new NLCPGNodeDraft(
                  Kind: NLCPGNodeKind.SyntaxToken,
                  Name: childToken.ValueText.Length > 0 ? childToken.ValueText : childToken.Text,
                  FilePath: filePath,
                  SpanStart: childToken.SpanStart,
                  SpanEnd: childToken.Span.End,
                  StableIdentityText: childToken.Kind().ToString()));
                graph.AddEdge(syntaxNode, tokenNode, NLCPGEdgeKind.TokenChild);
                tokenCount += 1;
            }

            tokenStopwatch.Stop();
            metrics.EmitChildTokensElapsedMilliseconds += tokenStopwatch.ElapsedMilliseconds;
            metrics.SyntaxTokenCount += tokenCount;
        }
    }
}
