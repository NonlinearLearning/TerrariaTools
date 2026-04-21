using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class GraphNodeConventionsTests
{
    [Fact]
    public void AppendNextCfgNodeId_addsDistinctTargetOnlyOnce()
    {
        CpgNode node = new(1, CpgNodeKind.Block);

        GraphNodeConventions.AppendNextCfgNodeId(node, 2);
        GraphNodeConventions.AppendNextCfgNodeId(node, 2);

        Assert.True(node.TryGetProperty<IReadOnlyCollection<long>>("NextCfgNodeIds", out IReadOnlyCollection<long>? nextIds));
        Assert.Single(nextIds!);
    }

    [Fact]
    public void SetLocation_writesLineAndColumn()
    {
        CpgNode node = new(1, CpgNodeKind.Block);

        GraphNodeConventions.SetLocation(node, 3, 7);

        Assert.True(node.TryGetProperty<int>("Line", out int line));
        Assert.True(node.TryGetProperty<int>("Column", out int column));
        Assert.Equal(3, line);
        Assert.Equal(7, column);
    }
}
