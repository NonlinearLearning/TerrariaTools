using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把 delete-class 对象创建命中收束到局部声明点，
/// 让后续局部符号引用传播只依赖稳定 declarator，而不是具体 new 表达式形状。
public sealed class ClassObjectCreationDeclarationPropagationRule : RuleDefinitionPropagate
{
    private static readonly RuleConsumesContract ObjectCreationConsumes = new(new[]
    {
      new RuleConsumedSyntax(
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
        },
        new RuleSemanticTag("Class.ExpressionTarget"))
    });

    private static readonly RuleSemanticTag LocalDefinitionSemanticTag =
      new("Class.LocalDefinitionFromObjectCreation");

    private static readonly RuleProducesContract LocalDefinitionProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.VariableDeclarator },
          LocalDefinitionSemanticTag)
      });

    public override string CapabilityId { get; } = "propagate.type.object-creation-declaration";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NEW-DECL-001";

    public override RuleConsumesContract Consumes => ObjectCreationConsumes;

    public override RuleProducesContract Produces => LocalDefinitionProduces;


    public override string Name { get; } = "Propagate delete-class object creations to local declarators";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.VariableDeclarator
      };

    // 把对象创建命中收束到局部 declarator，后续符号传播只依赖稳定的定义点。
    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ObjectCreationExpressionSyntax and
                not ImplicitObjectCreationExpressionSyntax)
            {
                continue;
            }

            var declarator = FindInitializerDeclarator(seedMark.SyntaxNode);
            if (declarator is null)
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                declarator,
                "Object creation initializer is marked; propagate mark to local declarator.",
                RuleOutputKind.LocalDefinitionFromObjectCreation,
                LocalDefinitionSemanticTag),
              seedMark,
              1);
        }
    }

    private static VariableDeclaratorSyntax? FindInitializerDeclarator(SyntaxNode syntaxNode)
    {
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Value.Span.Contains(syntaxNode.Span) &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
            {
                return variableDeclarator;
            }
        }

        return null;
    }
}
