using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Concurrency;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class MemberAccessPass : INLCPGPass
    {
        internal static MemberAccessPass Instance { get; } = new();

        private MemberAccessPass()
        {
        }

        public string Name => nameof(MemberAccessPass);

        // 触发成员访问 pass，为字段和属性访问补成员节点与引用边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunMemberAccessPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        private sealed record MemberAccessFact(
          IOperation Operation,
          ISymbol MemberSymbol,
          ITypeSymbol? InstanceType);

        internal void RunMemberAccessPass(NLCPGBuildContext context)
        {
            // G0-P R-3：本阶段的规划已在【执行相位之前】完成并登记（见 PlanStagesBeforeExecution）。
            // 这里只消费既有 plan。
            var plan = TakeRecordedStagePlan(StageDependencyTable.Stage.MemberAccess);
            if (plan is null)
            {
                // 规划相位【只在无操作根时】返回 null（见 PlanMemberAccessStage）。
                // 若此处仍有操作根，说明规划相位没有覆盖本阶段 —— fail-closed，
                // 而不是静默跳过整阶段的成员访问边（正是 G0-P 要消灭的失效形态）。
                // ⚠ D1：必须检查**全部文档**（见 HasAnyOperationRootAcrossDocuments）。
                if (HasAnyOperationRootAcrossDocuments(context))
                {
                    throw new InvalidOperationException(
                      "MemberAccess 阶段被请求执行，但规划相位没有登记它的 plan（G0-P R-3）。"
                      + "规划必须在任何 worker 启动之前完成；此处不重新规划，以免把规划时点拉回执行相位。");
                }

                return;
            }

            CommitMemberAccessStage(context, plan);
        }

        /// <summary>
        /// G0-P **R-3** 规划步：**只读**——算出批次，不触碰图。
        /// <para>
        /// 由 <c>PlanStagesBeforeExecution</c> 在**执行相位之前**调用。
        /// 返回 <c>null</c> 表示无操作根（与拆分前的提前 <c>return</c> 语义一致）。
        /// </para>
        /// <para>
        /// <b>为何可静态规划（源码确证）：</b>本阶段的批次来自 <c>AssembleWorkBatches(context)</c>，
        /// 其唯一输入是 <c>GetOperationRootPlans(root, semanticModel)</c>（纯语法/语义推导）
        /// 与无实例状态的 <c>CpgWorkBatchBuilder</c>；**不读任何前序 pass 的运行期产物**。
        /// </para>
        /// </summary>
        private StagePlan? PlanMemberAccessStage(NLCPGBuildContext context)
        {
            // ⚠ D1：判据必须是**全部文档**是否有操作根（见 HasAnyOperationRootAcrossDocuments）。
            if (!HasAnyOperationRootAcrossDocuments(context))
            {
                return null;
            }

            return new StagePlan(
              StageDependencyTable.Stage.MemberAccess,
              // D1：跨文件聚合装箱（单文件时与 AssembleWorkBatches 逐字相同）。
              AssembleWorkBatchesAcrossDocuments(context));
        }

        // 保留逐项路径作为迁移期间的语义对照实现。
        private void RunMemberAccessPassOrderedCompatibility(NLCPGBuildContext context)
        {
            foreach (var fieldReferenceOperation in context.FieldReferenceOperations)
            {
                var operationNode = GetOrCreateOperationNode(fieldReferenceOperation, context.Graph);
                AddMemberAccess(
                  fieldReferenceOperation,
                  operationNode,
                  fieldReferenceOperation.Field,
                  fieldReferenceOperation.Instance?.Type,
                  context.Graph);
            }

            foreach (var propertyReferenceOperation in context.PropertyReferenceOperations)
            {
                var operationNode = GetOrCreateOperationNode(propertyReferenceOperation, context.Graph);
                AddMemberAccess(
                  propertyReferenceOperation,
                  operationNode,
                  propertyReferenceOperation.Property,
                  propertyReferenceOperation.Instance?.Type,
                  context.Graph);
            }
        }

        /// <summary>
        /// G0-P **R-3** 提交步：消费 plan，执行 worker 并发布（**唯一写图者**）。
        /// <para>
        /// ⚠ 事实抽取（<c>memberFactsByRoot</c>）刻意留在**提交步**而**不**进规划步：
        /// 规划只产出批次与预估（只读、廉价），而事实抽取会遍历全部字段/属性访问操作；
        /// 把它前移会改变构建期的内存与耗时分布，超出 R-3「只前移规划时点」的范围。
        /// </para>
        /// </summary>
        private void CommitMemberAccessStage(NLCPGBuildContext context, StagePlan plan)
        {
            // D1：事实提取**逐文件**进行。Order 是文件内局部序号，且一个批次可跨文件，
            //   故索引必须是「文件 → order → 事实」；单一扁平表在跨文件下会把 B 的
            //   序号解析成 A 的语法体（静默错误：抽到别人的访问事实）。
            var memberFactsByFile = new Dictionary<string, Dictionary<int, IReadOnlyList<MemberAccessFact>>>(
              StringComparer.Ordinal);
            foreach (var document in context.Documents)
            {
                var operationRoots = GetOperationRootPlans(document.Root, document.SemanticModel);
                memberFactsByFile[document.FilePath] = operationRoots.ToDictionary(
                  root => root.Order,
                  root => CollectMemberAccessFacts(root.BodySyntax, document));
            }

            var workBatches = plan.Batches;
            // G0-P R-2：该阶段的实际产出是 MemberAccessFact（不是 LocalCpgFragment）——
            // 与 ControlFlow/ControlDependence 的 fragment 形态不同。acknowledged 记账用事实条数。
            var reportedFactCount = 0L;
            _workBatchExecutor.ExecuteAsync(
              workBatches,
              // worker 步：按文件分离事实（一个批次可跨文件——T3 已放宽装箱）。
              (batch, _, _) => CollectMemberAccessWorkBatch(batch, memberFactsByFile),
              // 归并步：把事实发布到各自所属文件的图（D1 按项路由）。
              // 旧实现直接写 context.Graph，多文件下会把 B 文件的事实写进 A 文件的图。
              groups =>
              {
                  foreach (var group in groups)
                  {
                      var graph = context.ResolveGraph(group.SourceFilePath);
                      foreach (var fact in group.Items)
                      {
                          var operationNode = GetOrCreateOperationNode(fact.Operation, graph);
                          AddMemberAccess(
                            fact.Operation,
                            operationNode,
                            fact.MemberSymbol,
                            fact.InstanceType,
                            graph);
                          reportedFactCount += 1;
                      }
                  }
              },
              stageId: CpgWorkBatchPerformanceStageId.MemberAccess).GetAwaiter().GetResult();
            // G0-P R-2：回报本次实际发布的事实条数，供统一记账。
            ReportMemberAccessFacts(context, reportedFactCount);
        }

        /// <summary>
        /// worker 步：按 <see cref="CpgWorkItem.SourceFilePath"/> 把批次事实分离成每文件一组。
        /// </summary>
        /// <remarks>
        /// 单文件批次下恒只有一组，行为与改造前逐字一致（组内次序也保持
        /// <c>OrderBy(StableOrder)</c> 后 <c>SelectMany</c> 的原语义）。
        /// </remarks>
        private static IReadOnlyList<SourceRoutedGroup<MemberAccessFact>> CollectMemberAccessWorkBatch(
          CpgWorkBatch batch,
          IReadOnlyDictionary<string, Dictionary<int, IReadOnlyList<MemberAccessFact>>> memberFactsByFile)
        {
            var groups = new List<SourceRoutedGroup<MemberAccessFact>>();
            foreach (var group in SourceFilePartition.BySourceFile(batch.Items))
            {
                // ⚠ 用**该文件自己的** order→事实 索引（D1）。
                var factsByRoot = memberFactsByFile.TryGetValue(group.SourceFilePath, out var fileFacts)
                  ? fileFacts
                  : new Dictionary<int, IReadOnlyList<MemberAccessFact>>();
                var facts = group.Items
                  .OrderBy(item => item.StableOrder)
                  .SelectMany(item => factsByRoot.GetValueOrDefault(item.StableOrder) ?? Array.Empty<MemberAccessFact>())
                  .ToArray();
                groups.Add(new SourceRoutedGroup<MemberAccessFact>(group.SourceFilePath, facts));
            }

            return groups;
        }

        private static IReadOnlyList<MemberAccessFact> CollectMemberAccessFacts(
          SyntaxNode bodySyntax,
          NLCPGBuildContext context)
        {
            var facts = new List<MemberAccessFact>();
            foreach (var fieldReferenceOperation in context.FieldReferenceOperations)
            {
                if (IsWithinBody(fieldReferenceOperation.Syntax, bodySyntax))
                {
                    facts.Add(new MemberAccessFact(
                      fieldReferenceOperation,
                      fieldReferenceOperation.Field,
                      fieldReferenceOperation.Instance?.Type));
                }
            }

            foreach (var propertyReferenceOperation in context.PropertyReferenceOperations)
            {
                if (IsWithinBody(propertyReferenceOperation.Syntax, bodySyntax))
                {
                    facts.Add(new MemberAccessFact(
                      propertyReferenceOperation,
                      propertyReferenceOperation.Property,
                      propertyReferenceOperation.Instance?.Type));
                }
            }

            return facts
              .OrderBy(fact => fact.Operation.Syntax.SpanStart)
              .ThenBy(fact => fact.Operation.Syntax.Span.End)
              .ThenBy(fact => fact.MemberSymbol.Kind)
              .ThenBy(fact => fact.MemberSymbol.Name, StringComparer.Ordinal)
              .ToArray();
        }

        private static bool IsWithinBody(SyntaxNode syntax, SyntaxNode bodySyntax)
        {
            return syntax.SpanStart >= bodySyntax.SpanStart && syntax.Span.End <= bodySyntax.Span.End;
        }

        private void AddMemberAccess(IOperation operation, NLCPGNode operationNode, ISymbol memberSymbol, ITypeSymbol? instanceType, NLCPGGraph graph)
        {
            var memberAccessNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: NLCPGNodeKind.MemberAccess,
              Name: memberSymbol.Name,
              FullName: ComposeMemberAccessFullName(memberSymbol, instanceType),
              Signature: ComposeSignature(memberSymbol),
              TypeFullName: ComposeTypeFullName(SymbolTypeOf(memberSymbol)),
              FilePath: graph.ResolveFilePath(operationNode),
              SpanStart: operationNode.SpanStart,
              SpanEnd: operationNode.SpanEnd));
            graph.AddEdge(operationNode, memberAccessNode, NLCPGEdgeKind.AccessesMember);

            var memberNode = GetOrCreateSymbolNode(memberSymbol, graph);
            graph.AddEdge(memberAccessNode, memberNode, NLCPGEdgeKind.Ref);
            AddEvalTypeEdge(memberAccessNode, SymbolTypeOf(memberSymbol), graph);
        }

        private string ComposeMemberAccessFullName(ISymbol memberSymbol, ITypeSymbol? instanceType)
        {
            var baseType = ResolveAccessBaseType(instanceType, memberSymbol);
            if (string.IsNullOrEmpty(baseType))
            {
                return ComposeFullName(memberSymbol);
            }

            return memberSymbol switch
            {
                IPropertySymbol propertySymbol when propertySymbol.Parameters.Length > 0 =>
                  $"{baseType}.{propertySymbol.Name}:{ComposePropertySignature(propertySymbol)}",
                _ => $"{baseType}.{memberSymbol.Name}",
            };
        }

        private string ResolveAccessBaseType(ITypeSymbol? instanceType, ISymbol memberSymbol)
        {
            if (instanceType is INamedTypeSymbol namedInstanceType &&
                CanUseReceiverTypeForMemberAccessFullName(namedInstanceType, memberSymbol))
            {
                return ComposeTypeFullName(namedInstanceType);
            }

            return ResolveDeclaringType(instanceType, memberSymbol);
        }

        private string ResolveDeclaringType(ITypeSymbol? instanceType, ISymbol memberSymbol)
        {
            if (instanceType is not null)
            {
                var candidate = ResolveDeclaredType(instanceType, memberSymbol);
                if (candidate is not null)
                {
                    return ComposeTypeFullName(candidate);
                }
            }

            return memberSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(memberSymbol.ContainingType);
        }

        private INamedTypeSymbol? ResolveDeclaredType(ITypeSymbol instanceType, ISymbol memberSymbol)
        {
            if (instanceType is not INamedTypeSymbol namedInstanceType)
            {
                return memberSymbol.ContainingType ?? instanceType as INamedTypeSymbol;
            }

            if (memberSymbol.ContainingType is not null &&
                SymbolEqualityComparer.Default.Equals(memberSymbol.ContainingType, namedInstanceType))
            {
                return namedInstanceType;
            }

            var memberName = memberSymbol.Name;
            // 先尝试命中符号原始声明类型，避免后续按继承关系误选更宽泛的类型。
            var exactContainingTypeCandidate = _declaredTypes.FirstOrDefault(declaredType =>
              memberSymbol.ContainingType is not null &&
              SymbolEqualityComparer.Default.Equals(declaredType, memberSymbol.ContainingType));
            if (exactContainingTypeCandidate is not null)
            {
                return exactContainingTypeCandidate;
            }

            // 再尝试接收者本身是否就是声明了兼容成员的内部类型。
            var exactReceiverCandidate = _declaredTypes.FirstOrDefault(declaredType =>
              SymbolEqualityComparer.Default.Equals(declaredType, namedInstanceType) &&
              DeclaresCompatibleMember(declaredType, memberSymbol, memberName));
            if (exactReceiverCandidate is not null)
            {
                return exactReceiverCandidate;
            }

            // 最后沿工程内继承关系找第一个兼容宿主。
            foreach (var declaredType in _declaredTypes)
            {
                if (!InheritsFrom(namedInstanceType, declaredType) ||
                    !DeclaresCompatibleMember(declaredType, memberSymbol, memberName))
                {
                    continue;
                }

                return declaredType;
            }

            return memberSymbol.ContainingType ?? namedInstanceType;
        }

        private static bool CanUseReceiverTypeForMemberAccessFullName(INamedTypeSymbol instanceType, ISymbol memberSymbol)
        {
            if (memberSymbol.ContainingType is null)
            {
                return true;
            }

            return InheritsFrom(instanceType, memberSymbol.ContainingType) ||
                   InheritsFrom(memberSymbol.ContainingType, instanceType);
        }

        private static bool DeclaresCompatibleMember(INamedTypeSymbol declaredType, ISymbol memberSymbol, string memberName)
        {
            foreach (var candidateMember in declaredType.GetMembers(memberName))
            {
                if (candidateMember.Kind != memberSymbol.Kind)
                {
                    continue;
                }

                if (MembersMatch(candidateMember, memberSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MembersMatch(ISymbol candidateMember, ISymbol memberSymbol)
        {
            if (SymbolEqualityComparer.Default.Equals(candidateMember, memberSymbol))
            {
                return true;
            }

            return (candidateMember, memberSymbol) switch
            {
                (IPropertySymbol candidateProperty, IPropertySymbol targetProperty) =>
                  string.Equals(ComposePropertySignature(candidateProperty), ComposePropertySignature(targetProperty), StringComparison.Ordinal),
                (IFieldSymbol candidateField, IFieldSymbol targetField) =>
                  string.Equals(ComposeTypeFullName(candidateField.Type), ComposeTypeFullName(targetField.Type), StringComparison.Ordinal),
                _ => false,
            };
        }
    }
}
