using NLISSN.Core.Pipeline;
using NLISSN.Hosting;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class CommandHostCompositionTests
{
    [Fact]
    public void DefaultConstructorUsesTheGeneratedCoreCatalog()
    {
        var result = new CommandHost().AnalyzeFromArgs(new[] { "--skip-rewrite" });

        Assert.Contains(
          result.RuleGraphNodeStatuses!.Keys,
          node => node == RuleNodeId.For(RuleKind.Mark, "mark.target.identifier-name"));
        Assert.DoesNotContain(
          result.RuleGraphNodeStatuses.Keys,
          node => node == RuleNodeId.For(RuleKind.Mark, "mark.unreachable-method"));
    }
}
