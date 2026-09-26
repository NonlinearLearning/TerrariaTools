using System.Text;
using NLISSN.Application;
using Xunit;

namespace RoslynPrototype.Tests.Analysis;

/// <summary>
/// <see cref="DocumentShardPlanner"/> 的单元测试。
/// <para>
/// 最关键的守护项是**完整函数规则**：超过目标片行数的方法必须独占一片、
/// 允许超标且永不被切分；计划器**不得**承诺消除超标项。
/// </para>
/// </summary>
public sealed class DocumentShardPlannerTests
{
    private static readonly DocumentShardPlannerOptions TestOptions = new(
      smallFileMaxLines: 100,
      mediumFileMaxLines: 400,
      largeFileShardTargetLines: 500);

    [Fact]
    public void Plan_SmallFile_ReturnsSingleShardContainingEveryMethod()
    {
        var methods = CreateMethods((0, 40), (1, 30), (2, 20));

        var plan = DocumentShardPlanner.Plan("small.cs", methods, TestOptions);

        Assert.Equal(FileSizeClass.Small, plan.SizeClass);
        var shard = Assert.Single(plan.Shards);
        Assert.Equal(0, shard.ShardIndex);
        Assert.Equal(new[] { 0, 1, 2 }, shard.MethodOrders);
        Assert.Equal(90, shard.EstimatedCost);
    }

    [Fact]
    public void Plan_MediumFile_KeepsOneShardEvenWhenAboveSmallThreshold()
    {
        var methods = CreateMethods((0, 200), (1, 150));

        var plan = DocumentShardPlanner.Plan("medium.cs", methods, TestOptions);

        Assert.Equal(FileSizeClass.Medium, plan.SizeClass);
        Assert.Single(plan.Shards);
        Assert.Equal(new[] { 0, 1 }, plan.Shards[0].MethodOrders);
    }

    [Fact]
    public void Plan_LargeFile_PacksIntoMultipleShardsThatRespectTheTarget()
    {
        // 12 × 60 = 720 行 > 中文件上限 400 ⇒ 大文件；目标片 500 行 ⇒ 期望 K=2（480+240）。
        var methods = CreateEvenMethods(count: 12, costEach: 60);

        var plan = DocumentShardPlanner.Plan("large.cs", methods, TestOptions);

        Assert.Equal(FileSizeClass.Large, plan.SizeClass);
        Assert.True(plan.Shards.Count > 1, "大文件必须被拆成多片。");
        Assert.All(plan.Shards, shard => Assert.True(
          shard.EstimatedCost <= TestOptions.LargeFileShardTargetLines,
          $"未含超标方法的分片不得超出目标：{shard.EstimatedCost}"));
        Assert.Equal(720, plan.Shards.Sum(shard => shard.EstimatedCost));
    }

    [Fact]
    public void Plan_SingleOverTargetMethod_IsIsolatedAndAllowedToExceedTarget()
    {
        // 方法 3 自身 5000 行 ≫ 目标 500 行：完整函数规则要求它独占一片且不被切分。
        var methods = CreateMethods((0, 60), (1, 60), (2, 60), (3, 5000), (4, 60), (5, 60));

        var plan = DocumentShardPlanner.Plan("oversized.cs", methods, TestOptions);

        Assert.Equal(FileSizeClass.Large, plan.SizeClass);
        var isolated = Assert.Single(plan.Shards.Where(shard => shard.MethodOrders.Contains(3)));
        Assert.Equal(new[] { 3 }, isolated.MethodOrders);
        Assert.Equal(5000, isolated.EstimatedCost);
        Assert.True(
          isolated.EstimatedCost > TestOptions.LargeFileShardTargetLines,
          "超标方法所在分片必须被允许超出目标片行数。");
        Assert.DoesNotContain(
          plan.Shards,
          shard => shard.EstimatedCost > TestOptions.LargeFileShardTargetLines &&
                   !shard.MethodOrders.Contains(3));
    }

    [Fact]
    public void Plan_EveryMethodOrder_AppearsInExactlyOneShard()
    {
        var methods = CreateMethods((0, 300), (1, 700), (2, 120), (3, 90), (4, 1500), (5, 45));

        var plan = DocumentShardPlanner.Plan("partition.cs", methods, TestOptions);

        var flattened = plan.Shards.SelectMany(shard => shard.MethodOrders).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, flattened.OrderBy(order => order).ToArray());
        Assert.Equal(flattened.Length, flattened.Distinct().Count());
    }

    [Fact]
    public void Plan_SameInputTwice_ProducesIdenticalPlan()
    {
        var methods = CreateMethods(
          (0, 300), (1, 120), (2, 700), (3, 90), (4, 1500), (5, 45), (6, 610));

        var first = DocumentShardPlanner.Plan("stable.cs", methods, TestOptions);
        var second = DocumentShardPlanner.Plan("stable.cs", methods, TestOptions);

        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public void Plan_ScrambledMethodOrderInput_ProducesSamePlanAsSortedInput()
    {
        var ascending = CreateMethods((0, 300), (1, 120), (2, 700), (3, 90), (4, 1500), (5, 45));
        var scrambled = CreateMethods((4, 1500), (1, 120), (5, 45), (0, 300), (3, 90), (2, 700));

        var fromAscending = DocumentShardPlanner.Plan("shuffled.cs", ascending, TestOptions);
        var fromScrambled = DocumentShardPlanner.Plan("shuffled.cs", scrambled, TestOptions);

        Assert.Equal(Describe(fromAscending), Describe(fromScrambled));
    }

    [Fact]
    public void Plan_ShardIndexes_AreContiguousStartingAtZero()
    {
        var methods = CreateEvenMethods(count: 16, costEach: 100);

        var plan = DocumentShardPlanner.Plan("indexes.cs", methods, TestOptions);

        Assert.Equal(
          Enumerable.Range(0, plan.Shards.Count).ToArray(),
          plan.Shards.Select(shard => shard.ShardIndex).ToArray());
    }

    [Fact]
    public void Plan_SingleFileOrdinal_MakesStableOrderStrictlyIncreasing()
    {
        var methods = CreateEvenMethods(count: 12, costEach: 100);

        var plan = DocumentShardPlanner.Plan("monotonic.cs", methods, TestOptions, fileOrdinal: 0);

        var stableOrders = plan.Shards.Select(shard => shard.StableOrder).ToArray();
        Assert.Equal(stableOrders.OrderBy(order => order).ToArray(), stableOrders);
        Assert.Equal(stableOrders.Length, stableOrders.Distinct().Count());
    }

    [Fact]
    public void PlanAll_MultipleLargeFiles_AssignsGloballyUniqueMonotonicStableOrders()
    {
        var files = new[]
        {
            new DocumentShardInputFile("a.cs", CreateEvenMethods(count: 12, costEach: 60)),
            new DocumentShardInputFile("b.cs", CreateEvenMethods(count: 12, costEach: 60)),
        };

        var plans = DocumentShardPlanner.PlanAll(files, TestOptions);

        var allStableOrders = plans
          .SelectMany(plan => plan.Shards)
          .Select(shard => shard.StableOrder)
          .ToArray();
        Assert.Equal(allStableOrders.Length, allStableOrders.Distinct().Count());
        Assert.Equal(allStableOrders.OrderBy(order => order).ToArray(), allStableOrders);

        // 文件序号按路径序数序分配：a.cs 的每一片都必须排在 b.cs 的每一片之前。
        var planA = plans.Single(plan => string.Equals(
          plan.FilePath, "a.cs", StringComparison.Ordinal));
        var planB = plans.Single(plan => string.Equals(
          plan.FilePath, "b.cs", StringComparison.Ordinal));
        Assert.True(
          planA.Shards.Max(shard => shard.StableOrder) <
          planB.Shards.Min(shard => shard.StableOrder));
    }

    [Fact]
    public void PlanAll_PermutedInputOrder_KeepsPerFileStableOrdersUnchanged()
    {
        var ascendingInput = new[]
        {
            new DocumentShardInputFile("a.cs", CreateEvenMethods(count: 12, costEach: 60)),
            new DocumentShardInputFile("b.cs", CreateEvenMethods(count: 12, costEach: 60)),
        };
        var permutedInput = new[]
        {
            ascendingInput[1],
            ascendingInput[0],
        };

        var ascending = DocumentShardPlanner.PlanAll(ascendingInput, TestOptions)
          .ToDictionary(plan => plan.FilePath, Describe, StringComparer.Ordinal);
        var permuted = DocumentShardPlanner.PlanAll(permutedInput, TestOptions)
          .ToDictionary(plan => plan.FilePath, Describe, StringComparer.Ordinal);

        Assert.Equal(ascending["a.cs"], permuted["a.cs"]);
        Assert.Equal(ascending["b.cs"], permuted["b.cs"]);
    }

    [Fact]
    public void Plan_EmptyMethodList_ReturnsOneEmptySmallShard()
    {
        var plan = DocumentShardPlanner.Plan(
          "empty.cs",
          Array.Empty<DocumentMethodDescriptor>(),
          TestOptions);

        Assert.Equal(FileSizeClass.Small, plan.SizeClass);
        var shard = Assert.Single(plan.Shards);
        Assert.Empty(shard.MethodOrders);
        Assert.Equal(0, shard.EstimatedCost);
        Assert.Equal(0L, shard.EstimatedBytes);
        Assert.Equal(0L, plan.EstimatedBytes);
    }

    [Fact]
    public void Plan_EstimatedBytes_UsesSixtyFourBytesPerCostUnitAndSumsIntoThePlan()
    {
        var methods = CreateMethods((0, 40), (1, 30));

        var plan = DocumentShardPlanner.Plan("bytes.cs", methods, TestOptions);

        Assert.Equal(64, DocumentShardPlanner.EstimatedBytesPerCostUnit);
        var shard = Assert.Single(plan.Shards);
        Assert.Equal(70L * DocumentShardPlanner.EstimatedBytesPerCostUnit, shard.EstimatedBytes);
        Assert.Equal(plan.Shards.Sum(item => item.EstimatedBytes), plan.EstimatedBytes);
    }

    [Theory]
    [InlineData(100, FileSizeClass.Small)]
    [InlineData(101, FileSizeClass.Medium)]
    [InlineData(400, FileSizeClass.Medium)]
    [InlineData(401, FileSizeClass.Large)]
    public void Classify_ThresholdBoundary_ReturnsExpectedClass(
      int totalCost,
      FileSizeClass expected)
    {
        Assert.Equal(expected, DocumentShardPlanner.Classify(totalCost, TestOptions));
    }

    [Fact]
    public void Plan_DuplicateMethodOrder_Throws()
    {
        var methods = CreateMethods((0, 60), (0, 60));

        Assert.Throws<ArgumentOutOfRangeException>(
          () => DocumentShardPlanner.Plan("duplicate.cs", methods, TestOptions));
    }

    [Fact]
    public void Plan_InvalidArguments_Throw()
    {
        var methods = CreateMethods((0, 60));
        var zeroCost = CreateMethods((0, 60), (1, 0));

        Assert.Throws<ArgumentException>(
          () => DocumentShardPlanner.Plan(" ", methods, TestOptions));
        Assert.Throws<ArgumentNullException>(
          () => DocumentShardPlanner.Plan("null-methods.cs", null!, TestOptions));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentShardPlanner.Plan(
          "negative-ordinal.cs", methods, TestOptions, fileOrdinal: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
          () => DocumentShardPlanner.Plan("zero-cost.cs", zeroCost, TestOptions));
    }

    [Fact]
    public void DocumentShardPlannerOptions_MediumNotAboveSmall_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
          () => new DocumentShardPlannerOptions(
            smallFileMaxLines: 200,
            mediumFileMaxLines: 200,
            largeFileShardTargetLines: 1500));
        Assert.Throws<ArgumentOutOfRangeException>(
          () => new DocumentShardPlannerOptions(
            smallFileMaxLines: 0,
            mediumFileMaxLines: 800,
            largeFileShardTargetLines: 1500));
        Assert.Throws<ArgumentOutOfRangeException>(
          () => new DocumentShardPlannerOptions(
            smallFileMaxLines: 200,
            mediumFileMaxLines: 800,
            largeFileShardTargetLines: 0));
    }

    [Fact]
    public void DocumentShardPlannerOptions_Default_UsesDocumentedCandidateThresholds()
    {
        var options = DocumentShardPlannerOptions.Default;

        Assert.Equal(200, options.SmallFileMaxLines);
        Assert.Equal(800, options.MediumFileMaxLines);
        Assert.Equal(1500, options.LargeFileShardTargetLines);
    }

    /// <summary>
    /// 把整份计划投影成字符串。**必须**逐字段投影而不是直接比较计划对象：
    /// <see cref="DocumentShardPlan"/> 的 <c>Shards</c> 是集合成员，<c>record</c> 生成的
    /// <c>Equals</c> 对它只做引用比较，两份内容相同但分别构造的计划**不**相等。
    /// </summary>
    private static string Describe(DocumentShardPlan plan)
    {
        var builder = new StringBuilder();
        builder
          .Append(plan.FilePath).Append('|')
          .Append(plan.SizeClass).Append('|')
          .Append(plan.EstimatedBytes);
        foreach (var shard in plan.Shards)
        {
            builder
              .Append("||")
              .Append(shard.StableOrder).Append(',')
              .Append(shard.ShardIndex).Append(',')
              .Append(shard.EstimatedCost).Append(',')
              .Append(shard.EstimatedBytes).Append(',')
              .Append(string.Join("-", shard.MethodOrders));
        }

        return builder.ToString();
    }

    private static DocumentMethodDescriptor[] CreateMethods(
      params (int MethodOrder, int Cost)[] methods)
    {
        return methods
          .Select(method => new DocumentMethodDescriptor(method.MethodOrder, method.Cost))
          .ToArray();
    }

    private static DocumentMethodDescriptor[] CreateEvenMethods(int count, int costEach)
    {
        return Enumerable
          .Range(0, count)
          .Select(order => new DocumentMethodDescriptor(order, costEach))
          .ToArray();
    }
}
