using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 删除类规则的 seed mark 入口：分别定位声明、表达式和 TypeSyntax，后续阶段再决定删除范围。

[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class TypeSyntaxMarkRule : RuleDefinitionMark
{
    private static readonly RuleFactKind TypeSyntaxFactKind = RuleFactKind.TargetTypeSyntax;
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.QualifiedName,
        SyntaxKind.AliasQualifiedName,
        SyntaxKind.GenericName
      };


    public override string RuleId { get; } = "mark.type.type-syntax";


    public override string Name { get; } = "Match type syntax that references the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;
    public override RuleProducesContract Produces { get; } = new(new[]
    {
        new RuleProducedSyntax(SupportedKinds, TypeSyntaxFactKind)
    });

    // 标记处在声明位置上的目标类 TypeSyntax，供声明宿主传播规则继续收束。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        return DeclarationMarkRuleHelpers.BuildTypeSyntaxMarks(
          context,
          root,
          RuleId,
          SupportedKinds)
          .Select(mark => mark with
          {
            FactKind = TypeSyntaxFactKind,
            Origins = RuleEvidenceOrigin.DeclarationType
          });
    }
}

