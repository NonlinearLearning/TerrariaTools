using NLISSN.Rules;

namespace NLISSN.Core.Propagation;

public abstract class ClassPropagationRuleBase : RuleDefinitionPropagate
{
    private static readonly RuleTerminalConsumesContract ClassSeedFacts = new(
      new[]
      {
        new RuleTerminalFactSelector(
          RuleFactDomain.Class,
          new[] { RuleKind.Mark })
      });

    public override RuleFactDomain FactDomain => RuleFactDomain.Class;

    public override RuleTerminalConsumesContract TerminalConsumes => ClassSeedFacts;
}
