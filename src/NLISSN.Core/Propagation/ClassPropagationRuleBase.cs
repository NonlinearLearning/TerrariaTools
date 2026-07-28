using NLISSN.Rules;

namespace NLISSN.Core.Propagation;

public abstract class ClassPropagationRuleBase : RuleDefinitionPropagate
{
    public override IReadOnlyList<RuleDependency> Dependencies { get; } =
      new[]
      {
        "DEL-CLASS-MARK-DECL-001",
        "DEL-CLASS-MARK-EXPR-001",
        "DEL-CLASS-MARK-TYPE-001"
      }
      .Select(ruleId => new RuleDependency(RuleNodeId.For(RuleKind.Mark, ruleId), RuleOutputKind.SeedMark))
      .ToList();
}
