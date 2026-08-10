using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把 delete-class 对象创建命中收束到局部声明点，
/// 让后续局部符号引用传播只依赖稳定 declarator，而不是具体 new 表达式形状。
public sealed class ObjectCreationDeclarationPropagationRule : RuleDefinitionPropagate
{
    private static readonly RuleConsumesContract ObjectCreationConsumes = new(new[]
    {
      new RuleConsumedSyntax(
        ExpressionFlowPropagationRuleBase.TargetExpressionNodeKinds,
        RuleFactPorts.TargetExpression)
    });

    private static readonly RuleSemanticTag LocalDefinitionSemanticTag =
      RuleFactPorts.FlowLocalDefinition;

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
    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ObjectCreationExpressionSyntax and
                not ImplicitObjectCreationExpressionSyntax)
            {
                continue;
            }

            var declarator = context.ResolveExpressionTopology((ExpressionSyntax)seedMark.SyntaxNode)
              .StructuralOwners
              .OfType<VariableDeclaratorSyntax>()
              .FirstOrDefault();
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

}
