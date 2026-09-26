using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgWorkBatchBuilderTests
{
    [Fact]
    public void Build_PacksSmallMethodsWithoutExceedingHardCost()
    {
        var options = new CpgWorkBatchCostOptions(
            smallMaxCost: 40,
            mediumMaxCost: 200,
            largeMaxCost: 800,
            targetBatchCost: 60,
            maxBatchCost: 120);
        var items = new[]
        {
            CreateItem(0, 30),
            CreateItem(1, 30),
            CreateItem(2, 30),
            CreateItem(3, 30),
        };

        var batches = new CpgWorkBatchBuilder(options).Build("sample.cs", items);

        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch => Assert.InRange(batch.EstimatedCost, 1, 120));
        Assert.Equal(new[] { 0, 1 }, batches[0].Items.Select(item => item.StableOrder));
        Assert.Equal(new[] { 2, 3 }, batches[1].Items.Select(item => item.StableOrder));
    }

    [Fact]
    public void Build_DoesNotGroupLargeMethodsTogether()
    {
        var items = new[]
        {
            CreateItem(0, 201),
            CreateItem(1, 201),
            CreateItem(2, 20),
        };

        var batches = new CpgWorkBatchBuilder(CpgWorkBatchCostOptions.Default)
            .Build("sample.cs", items);

        Assert.Contains(batches, batch => batch.Items.Count == 1 && batch.Items[0].EstimatedCost == 201);
        Assert.Equal(3, batches.SelectMany(batch => batch.Items).Count());
    }

    [Fact]
    public void Build_LeavesOversizedMethodInAnIsolatedBatch()
    {
        var items = new[]
        {
            CreateItem(0, 801),
            CreateItem(1, 10),
        };

        var batches = new CpgWorkBatchBuilder(CpgWorkBatchCostOptions.Default)
            .Build("sample.cs", items);

        var oversized = Assert.Single(batches, batch => batch.Items[0].EstimatedCost == 801);
        Assert.Single(oversized.Items);
    }

    [Fact]
    public void Build_PlacesPreludeBeforeMethodsWithStableOrder()
    {
        var items = new[]
        {
            CreateItem(2, 10, CpgWorkItemKind.Method),
            CreateItem(0, 1, CpgWorkItemKind.FilePrelude),
            CreateItem(1, 1, CpgWorkItemKind.Declaration),
        };

        var batches = new CpgWorkBatchBuilder(CpgWorkBatchCostOptions.Default)
            .Build("sample.cs", items);

        Assert.Equal(CpgWorkBatchKind.Prelude, batches[0].Kind);
        Assert.Equal(new[] { 0, 1 }, batches[0].Items.Select(item => item.StableOrder));
        Assert.Equal(CpgWorkItemKind.Method, batches[1].Items[0].Kind);
    }

    [Fact]
    public void Build_DeduplicatesRepeatedMethodRoot()
    {
        var items = new[]
        {
            CreateItem(0, 10, CpgWorkItemKind.Method, "M:Outer"),
            CreateItem(1, 10, CpgWorkItemKind.Method, "M:Local"),
            CreateItem(2, 10, CpgWorkItemKind.Method, "M:Local"),
        };

        var batches = new CpgWorkBatchBuilder(CpgWorkBatchCostOptions.Default)
            .Build("sample.cs", items);

        Assert.Equal(new[] { "M:Outer", "M:Local" },
            batches.SelectMany(batch => batch.Items).Select(item => item.MethodSymbolKey));
    }

    [Fact]
    public void Build_IsIndependentOfInputEnumerationCompletionOrder()
    {
        var items = new[]
        {
            CreateItem(0, 10),
            CreateItem(1, 20),
            CreateItem(2, 30),
            CreateItem(3, 40),
        };
        var builder = new CpgWorkBatchBuilder(CpgWorkBatchCostOptions.Default);

        var forward = builder.Build("sample.cs", items);
        var reverse = builder.Build("sample.cs", items.Reverse());

        Assert.Equal(
            forward.Select(batch => string.Join(",", batch.Items.Select(item => item.StableSpanIdentity))),
            reverse.Select(batch => string.Join(",", batch.Items.Select(item => item.StableSpanIdentity))));
    }

    private static CpgWorkItem CreateItem(
        int stableOrder,
        int cost,
        CpgWorkItemKind kind = CpgWorkItemKind.Method,
        string? methodSymbolKey = null)
    {
        return new CpgWorkItem(
            stableOrder,
            "sample.cs",
            methodSymbolKey ?? $"M:{stableOrder}",
            stableOrder * 100,
            stableOrder * 100 + cost,
            cost,
            kind);
    }
}
