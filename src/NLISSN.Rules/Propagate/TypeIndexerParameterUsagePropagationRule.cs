using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为 indexer 参数删除收集声明宿主和访问点，保证提案阶段能同时改声明与所有受影响的 element access。
public sealed class ClassIndexerParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string CapabilityId { get; } = "propagate.type.indexer-parameter-usage";

    public override string RuleId { get; } = "DEL-CLASS-PROP-INDEXER-PARAM-USAGE-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Propagate delete-class indexer parameter usage to indexers and mapped access sites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    // 收集索引器声明与受影响访问点，供后续同步收缩签名和 element access。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.Indexer)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    payload.Indexer,
                    "Indexer parameter type references the delete-class target; propagate to the owning indexer declaration."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var access in payload.AccessCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(access)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    access,
                    "Indexer access passes the delete-class typed parameter; propagate to a shrinkable access site."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    /// 先匹配命名参数，再退回位置参数，
    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out IndexerParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, "DEL-CLASS-MARK-TYPE-001", StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentIndexerPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.Indexer,
              IndexerParameterUsageMode.NamedArgument,
              namedPlan.AccessRewrites);
            return true;
        }

        if (_analyzer.TryBuildIndexerPlan(context, typeSyntax, out var positionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              positionalPlan.Indexer,
              IndexerParameterUsageMode.Positional,
              positionalPlan.AccessRewrites);
            return true;
        }

        return false;
    }

    private static IndexerParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, IndexerDeclarationSyntax indexer, IndexerParameterUsageMode mode, IReadOnlyList<ElementAccessRewrite> accessRewrites)
    {
        var parameterIndex = indexer.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new IndexerParameterUsagePayload(
          indexer,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          accessRewrites.Select(rewrite => rewrite.ElementAccess).ToList());
    }
}
