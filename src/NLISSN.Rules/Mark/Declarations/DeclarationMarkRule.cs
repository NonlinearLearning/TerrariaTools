using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 删除类规则的 seed mark 入口：分别定位声明、表达式和 TypeSyntax，后续阶段再决定删除范围。

[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class DeclarationMarkRule : RuleDefinitionMark
{
    private static readonly RuleFactKind DeclarationTargetFactKind = RuleFactKind.TargetDeclaration;

    public override string RuleId { get; } = "mark.type.declaration";


    public override string Name { get; } = "Match class declarations by delete-class option";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.ClassDeclaration };
    public override RuleProducesContract Produces { get; } = new(new[]
    {
        new RuleProducedSyntax(new[] { SyntaxKind.ClassDeclaration }, DeclarationTargetFactKind)
    });

    // 仅对名称直接匹配 delete-class 目标的类声明生成 seed mark。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        return DeclarationMarkRuleHelpers.BuildDeclarationMarks(context, root, RuleId)
          .Select(mark => mark with
          {
            FactKind = DeclarationTargetFactKind,
            Origins = RuleEvidenceOrigin.DeclarationName
          });
    }
}

