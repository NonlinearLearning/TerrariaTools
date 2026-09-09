using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 删除类规则的 seed mark 入口：分别定位声明、表达式和 TypeSyntax，后续阶段再决定删除范围。

[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class TypedExpressionMarkRule : RuleDefinitionMark
{
    private static readonly RuleFactKind ExpressionTargetFactKind = RuleFactKind.TargetExpression;
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression
      };


    public override string RuleId { get; } = "mark.type.expression";


    public override string Name { get; } = "Match expressions that reference the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;
    public override RuleProducesContract Produces { get; } = new(new[]
    {
        new RuleProducedSyntax(SupportedKinds, ExpressionTargetFactKind)
    });

    // 标记语义上引用目标类的表达式，并过滤掉会被更大宿主覆盖的重复命中。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        return DeclarationMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          SupportedKinds)
          .Select(mark => mark with
          {
            FactKind = ExpressionTargetFactKind,
            Origins = RuleEvidenceOrigin.DeclarationExpression
          });
    }
}

