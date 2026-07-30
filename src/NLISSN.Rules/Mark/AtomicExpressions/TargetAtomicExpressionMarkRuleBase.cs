using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Rules;

namespace NLISSN.Core.Marking;

/// 统一原子表达式的标记入口：只产出可独立判断的表达式，不提前处理逻辑宿主或语句删除。
public abstract class SObjectAtomicExpressionMarkRuleBase : RuleDefinitionMark
{
    private const string DeleteSObjectGroupKey = "DEL-SOBJ";
    private static readonly RuleSemanticTag AtomicTargetSemanticTag = new("Target.Atomic");


    protected abstract SyntaxKind MarkKind { get; }

    public override RuleProducesContract Produces => new(new[]
    {
        new RuleProducedSyntax(new[] { MarkKind }, AtomicTargetSemanticTag)
    });

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { MarkKind };

    // 只为当前允许的原子表达式种类生成 seed mark，不提前扩展到更大的宿主结构。
    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteSObjectMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          AllowedMarkNodeKinds)
          .Select(mark => mark with { SemanticTag = AtomicTargetSemanticTag });
    }
}
