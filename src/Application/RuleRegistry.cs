using Rules;

namespace RoslynPrototype.Application;

public static class RuleRegistry
{
    public static IReadOnlyList<RuleDefinition> CreateDefaultRules()
    {
        return new RuleDefinition[]
        {
      new DeleteSObjectExpressionRule(),
      new DeleteUnreachableMethodRule()
        };
    }
}
