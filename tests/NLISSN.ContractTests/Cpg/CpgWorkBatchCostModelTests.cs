using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgWorkBatchCostModelTests
{
    [Theory]
    [InlineData(10, 10, 1)]
    [InlineData(10, 49, 40)]
    [InlineData(10, 50, 41)]
    public void Estimate_UsesInclusiveSourceLineCost(int startLine, int endLine, int expectedCost)
    {
        var estimate = CpgWorkBatchCostModel.Estimate(startLine, endLine);

        Assert.Equal(expectedCost, estimate.Cost);
        Assert.Equal(CpgWorkBatchCostFallbackReason.None, estimate.FallbackReason);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(20, 19)]
    public void Estimate_InvalidSpan_UsesMinimumCostAndReportsFallback(int startLine, int endLine)
    {
        var estimate = CpgWorkBatchCostModel.Estimate(startLine, endLine);

        Assert.Equal(1, estimate.Cost);
        Assert.Equal(CpgWorkBatchCostFallbackReason.UnknownSpanCost, estimate.FallbackReason);
    }

    [Theory]
    [InlineData(1, CpgWorkBatchSizeClass.Small)]
    [InlineData(40, CpgWorkBatchSizeClass.Small)]
    [InlineData(41, CpgWorkBatchSizeClass.Medium)]
    [InlineData(200, CpgWorkBatchSizeClass.Medium)]
    [InlineData(201, CpgWorkBatchSizeClass.Large)]
    [InlineData(800, CpgWorkBatchSizeClass.Large)]
    [InlineData(801, CpgWorkBatchSizeClass.Oversized)]
    public void Classify_UsesConfiguredFourSizeThresholds(int cost, CpgWorkBatchSizeClass expectedClass)
    {
        var actual = CpgWorkBatchCostModel.Classify(cost, CpgWorkBatchCostOptions.Default);

        Assert.Equal(expectedClass, actual);
    }

    [Fact]
    public void CostOptions_RejectNonPositiveThresholdsAndBudgets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpgWorkBatchCostOptions(
            smallMaxCost: 0,
            mediumMaxCost: 200,
            largeMaxCost: 800,
            targetBatchCost: 400,
            maxBatchCost: 800));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpgWorkBatchCostOptions(
            smallMaxCost: 40,
            mediumMaxCost: 200,
            largeMaxCost: 800,
            targetBatchCost: 0,
            maxBatchCost: 800));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpgWorkBatchCostOptions(
            smallMaxCost: 40,
            mediumMaxCost: 200,
            largeMaxCost: 800,
            targetBatchCost: 800,
            maxBatchCost: 400));
    }

    [Fact]
    public void WorkItems_RetainStableSourceOrderIndependentOfCompletionOrder()
    {
        var first = new CpgWorkItem(
            stableOrder: 0,
            sourceFilePath: "sample.cs",
            methodSymbolKey: "M:first",
            spanStart: 10,
            spanEnd: 20,
            estimatedCost: 11,
            kind: CpgWorkItemKind.Method);
        var second = new CpgWorkItem(
            stableOrder: 1,
            sourceFilePath: "sample.cs",
            methodSymbolKey: "M:second",
            spanStart: 30,
            spanEnd: 40,
            estimatedCost: 11,
            kind: CpgWorkItemKind.Method);

        var completionOrder = new[] { second, first };
        var stableOrder = completionOrder.OrderBy(item => item.StableOrder).ToArray();

        Assert.Equal(new[] { first.StableSpanIdentity, second.StableSpanIdentity },
            stableOrder.Select(item => item.StableSpanIdentity));
        Assert.NotEqual(first.StableSpanIdentity, second.StableSpanIdentity);
    }
}
