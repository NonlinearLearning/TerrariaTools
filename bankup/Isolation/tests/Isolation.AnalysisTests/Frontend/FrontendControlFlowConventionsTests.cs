using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class FrontendControlFlowConventionsTests
{
    [Fact]
    public void ControlNames_areStableConstants()
    {
        Assert.Equal("BLOCK", FrontendControlFlowConventions.Block);
        Assert.Equal("SWITCH_SECTION", FrontendControlFlowConventions.SwitchSection);
        Assert.Equal("IF", FrontendControlFlowConventions.If);
        Assert.Equal("TRY", FrontendControlFlowConventions.Try);
        Assert.Equal("FINALLY", FrontendControlFlowConventions.Finally);
    }

    [Fact]
    public void BuildDefaultControlType_normalizesSyntaxKindNames()
    {
        Assert.Equal("LOCKSTATEMENT", FrontendControlFlowConventions.BuildDefaultControlType("LockStatement"));
        Assert.Equal("<unknown>", FrontendControlFlowConventions.BuildDefaultControlType(null));
    }

    [Fact]
    public void TerminalKindChecks_matchExpectedKinds()
    {
        Assert.True(FrontendControlFlowConventions.IsReturnTerminal("return"));
        Assert.True(FrontendControlFlowConventions.IsBreakTerminal("break"));
        Assert.True(FrontendControlFlowConventions.IsContinueTerminal("continue"));
        Assert.False(FrontendControlFlowConventions.IsReturnTerminal("throw"));
    }
}
