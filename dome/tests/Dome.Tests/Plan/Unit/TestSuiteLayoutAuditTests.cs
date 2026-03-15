using TerrariaTools.Testing.Assertions;
using Xunit;

namespace TerrariaTools.Dome.Tests.Plan;

public sealed class TestSuiteLayoutAuditTests
{
    [Fact]
    public void DomeTests_FollowsStructuredLayoutRules()
    {
        TestSuiteLayoutAuditor.AssertDomeTestsStructure();
    }
}
