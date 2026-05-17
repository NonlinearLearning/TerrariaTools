using Rules;

namespace RoslynPrototype.Application;

public static class RuleRegistry
{
  public static IReadOnlyList<IDeletionRule> CreateDefaultRules()
  {
    return new IDeletionRule[]
    {
      new DeleteSObjectExpressionRule(),
      new DeleteUnreachableMethodRule()
    };
  }
}
