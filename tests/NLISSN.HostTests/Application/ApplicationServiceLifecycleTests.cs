using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Application;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Application;

/// <summary>
/// G0-L 回归：内部自建运行时的重载必须释放它自己创建的内核。
/// <para>
/// <see cref="ApplicationService.Analyze(string, string, NLISSN.Core.Pipeline.AnalysisRequestSettings)"/>
/// 与五参重载在内部调用 <c>AnalysisRuntimeFactory.CreateDefault()</c>，故按
/// 「内部创建的资源在返回前释放」的约定由它们负责释放内核。
/// </para>
/// <para>
/// ⚠️ <b>本文件的判别力边界（实测得出，不得外推）：</b>
/// 这里**没有**能判别「是否真的释放」的资源用例。<c>ThreadPool.ThreadCount</c>
/// 在「释放」与「不释放」两种情况下实测**都是 17 → 17（delta=0）**，
/// 因为 <c>WorkScheduler.WorkerLoopAsync</c> 在 <c>await gate.Task</c> 上让出，
/// 内核 worker 是**异步**的，众多逻辑 worker 由约 17 个物理线程服务，
/// 线程计数无法反映逻辑 worker 的堆积。
/// 释放接线本身由 <c>AnalysisRuntimeSchedulerTests</c> 的
/// <c>DisposeSchedulerAsync_*</c> 一组用例（含变异检查）覆盖；
/// 本文件只锁住「释放之后结果仍可用、重复调用与五参重载都仍正确」这一面，
/// 防止把释放接到结果产生之前而引入回归。
/// </para>
/// </summary>
public sealed class ApplicationServiceLifecycleTests
{
    private const string Source = """
      public sealed class LifecycleSample
      {
        public int M01(int value) => M02(value) + 1;
        public int M02(int value) => value > 0 ? M03(value) : 0;
        public int M03(int value) => value * 2;
      }
      """;

    private static Dictionary<string, string> CreateOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target-name"] = "LifecycleSample",
        };
    }

    /// <summary>强制枚举结果的各主要集合，确保没有指向已释放内核的惰性句柄。</summary>
    private static void ReadEveryCollection(PrototypeAnalysisResult result)
    {
        _ = result.SeedMarks.Count;
        _ = result.PropagatedMarks.Count;
        _ = result.LiftedMarks.Count;
        _ = result.Decisions.Count;
        _ = result.Edits.Count;
        _ = result.Diagnostics?.Count ?? 0;
        _ = result.GraphMetrics?.NodeCount ?? 0;
    }

    [Fact]
    public void Analyze_WhenCalledRepeatedlyThroughSelfBuiltRuntime_StillProducesReadableResults()
    {
        // 每次调用都自建并释放一个内核；重复调用必须仍然正确，
        // 且每次的结果都要在释放**之后**仍可读。
        var application = new ApplicationService(RulePipelineTestFactory.Create());
        var options = CreateOptions();

        for (var round = 0; round < 6; round++)
        {
            var result = application.Analyze(Source, $"lifecycle-{round}.cs", options);

            Assert.NotNull(result);
            ReadEveryCollection(result);
        }
    }

    [Fact]
    public void Analyze_WhenSelfBuiltRuntimeIsUsed_TheResultStaysReadableAfterDisposal()
    {
        // 释放必须发生在**结果产生之后**，否则调用方拿到的结果里
        // 任何指向内核的惰性句柄都会在读取时抛 ObjectDisposedException。
        var application = new ApplicationService(RulePipelineTestFactory.Create());

        var result = application.Analyze(Source, "lifecycle-usable.cs", CreateOptions());

        Assert.NotNull(result);
        ReadEveryCollection(result);

        // 再读一次，确保不是「第一次读碰巧成功」。
        ReadEveryCollection(result);
    }

    [Fact]
    public void Analyze_WhenFiveArgumentOverloadIsUsed_AlsoReturnsReadableResults()
    {
        // 五参重载同样内部自建 runtime，且它正是测试扩展方法实际走的那条路径。
        var application = new ApplicationService(RulePipelineTestFactory.Create());
        var options = CreateOptions();

        // 同一个语法树既提供 root 又提供 SemanticModel，否则
        // CSharpSemanticModel 会因「语法节点不在语法树中」拒绝。
        var tree = CSharpSyntaxTree.ParseText(Source);
        var compilation = CSharpCompilation.Create(
          "lifecycle-fivearg",
          new[] { tree },
          new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        for (var round = 0; round < 4; round++)
        {
            var result = application.Analyze(
              Source,
              $"lifecycle-fivearg-{round}.cs",
              options,
              semanticModel,
              root);

            Assert.NotNull(result);
            ReadEveryCollection(result);
        }
    }
}
