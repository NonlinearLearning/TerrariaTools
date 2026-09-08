using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 为 s-object 删除规则登记允许作为最小 seed mark 的原子表达式种类。

public sealed class AtomicVariableDeclaratorMarkRule : RuleDefinitionMark
{
    private static readonly RuleFactKind AtomicTargetFactKind = RuleFactKind.TargetExpression;

    public override string RuleId { get; } = "mark.target.variable-declarator";
    public override string Name { get; } = "Match s-rooted variable declarators";
    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } = new[] { SyntaxKind.VariableDeclarator };
    public override RuleProducesContract Produces { get; } = new(new[]
    {
        new RuleProducedSyntax(new[] { SyntaxKind.VariableDeclarator }, AtomicTargetFactKind)
    });

    // 把命中目标名称的变量定义点收束为 declarator，供后续符号传播沿定义继续扩展。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        return AtomicMarkRuleHelpers.BuildDefinitionLeftValueMarks(context, root, RuleId)
          .Select(mark => mark with
          {
            FactKind = AtomicTargetFactKind,
            Origins = RuleEvidenceOrigin.AtomicExpression
          });
    }
}

