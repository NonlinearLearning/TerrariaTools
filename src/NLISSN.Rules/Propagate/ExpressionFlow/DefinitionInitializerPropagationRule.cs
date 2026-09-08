using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.ExpressionPropagation;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把初始化表达式上的命中收束到变量声明点，避免后续规则直接依赖易碎的子表达式位置。
public sealed class DefinitionInitializerPropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleFactKind LocalDefinitionFactKind = RuleFactKind.FlowLocalDefinition;

    private static readonly RuleProducesContract LocalDefinitionProduces = new(new[]
    {
        new RuleProducedSyntax(new[] { SyntaxKind.VariableDeclarator }, LocalDefinitionFactKind)
    });

public override string RuleId { get; } = "propagate.target.definition-initializer";
    public override RuleProducesContract Produces => LocalDefinitionProduces;
    public override string Name { get; } = "Propagate s-object marks from definition initializers to declarators";

    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            var topology = context.ResolveExpressionTopology(expression);
            if (topology.Termination == ExpressionTopologyTermination.Terminal)
            {
                continue;
            }

            var variableDeclarator = topology.StructuralOwners.OfType<VariableDeclaratorSyntax>().FirstOrDefault();
            if (variableDeclarator is not null)
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    variableDeclarator,
                    "Definition initializer topology identifies the owning local definition.",
                    RuleOutputKind.LocalDefinitionFromInitializer,
                    factKind: LocalDefinitionFactKind),
                  seedMark,
                  1);
            }
        }
    }
}
