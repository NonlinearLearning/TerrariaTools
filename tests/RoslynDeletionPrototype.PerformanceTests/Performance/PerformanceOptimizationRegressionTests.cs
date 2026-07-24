using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Analysis;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.DeleteClassDirectory;
using RoslynPrototype.Tests.TestCodeSet.Performance;
using Rules;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

public sealed class PerformanceOptimizationRegressionTests : IDisposable
{
    private const int DiffPerformanceConsumerCount = 32;
    private const int DiffPerformanceReferencesPerConsumer = 32;
    private readonly string _tempDirectory;
    private readonly ITestOutputHelper _output;

    public PerformanceOptimizationRegressionTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirectory = Path.Combine(
          Path.GetTempPath(),
          $"roslyn-prototype-optimization-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void MarkPerformanceFixture_PreservesDopSnapshotsAndCollectsThreeWarmedSamples()
    {
        const string source = """
          public sealed class Box
          {
            public int Left { get; }
            public int Right { get; }
            public bool Ready { get; }
            public Box Next { get; }
          }

          public sealed class Sample
          {
            public int Run(Box s, Box other, bool fallback)
            {
              var value = s.Next.Left + s.Right + other.Left;
              return s.Ready && other.Ready && fallback ? value : s.Next.Right;
            }
          }
          """;
        var serial = MeasureMarkAnalysis(source, 1);
        _ = MeasureMarkAnalysis(source, 16);
        var parallelSamples = Enumerable.Range(0, 3)
          .Select(_ => MeasureMarkAnalysis(source, 16))
          .ToArray();

        Assert.All(parallelSamples, sample => Assert.Equal(serial.Snapshot, sample.Snapshot));
        Assert.All(parallelSamples, sample => Assert.True(sample.AllocatedBytes >= 0));
        Assert.All(parallelSamples, sample => Assert.True(sample.MarkTelemetry.AtomicCandidateCount > 0));
        Assert.All(parallelSamples, sample => Assert.True(sample.MarkTelemetry.TargetMatchQueryCount > 0));
        _output.WriteLine(
          $"Mark samples ms={string.Join(",", parallelSamples.Select(sample => sample.MarkMilliseconds))}; " +
          $"allocated={string.Join(",", parallelSamples.Select(sample => sample.AllocatedBytes))}");
    }

    [Fact]
    public void NamedArgumentMethodPlan_RewritesNamedCallsitesAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateNamedArgumentMethodPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildNamedArgumentMethodPlan(
          context.RuleContext,
          context.FindParameterTypeSyntax("Game.cs", "Apply", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Contains(
          "public int Apply(int frame)",
          plan.ReplacementMethod.NormalizeWhitespace().ToFullString(),
          StringComparison.Ordinal);
        Assert.Equal(
          new[]
          {
            "game.Apply(frame: 1)",
            "game.Apply(frame: frame)"
          },
          plan.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void OptionalParameterMethodPlan_KeepsOmittedCallsitesAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateOptionalParameterMethodPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildOptionalParameterMethodPlan(
          context.RuleContext,
          context.FindParameterTypeSyntax("Game.cs", "Apply", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Contains(
          "public int Apply(int frame, int scale = 1)",
          plan.ReplacementMethod.NormalizeWhitespace().ToFullString(),
          StringComparison.Ordinal);
        Assert.Equal(
          new[] { "game.Apply(frame, scale: 2)" },
          plan.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void ParamsMethodPlan_SucceedsWhenAllParamsArgumentsAreImplicitAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateImplicitParamsMethodPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildParamsMethodPlan(
          context.RuleContext,
          context.FindParameterTypeSyntax("Game.cs", "Apply", "inputs"),
          out var plan);

        Assert.True(succeeded);
        Assert.Contains(
          "public int Apply(int frame)",
          plan.ReplacementMethod.NormalizeWhitespace().ToFullString(),
          StringComparison.Ordinal);
        Assert.Empty(plan.InvocationRewrites);
    }

    [Fact]
    public void ParamsMethodPlan_FailsWhenAnyCallsiteSuppliesExplicitParamsArgument()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateExplicitParamsMethodPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildParamsMethodPlan(
          context.RuleContext,
          context.FindParameterTypeSyntax("Game.cs", "Apply", "inputs"),
          out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void NamedIndexerPlan_RewritesNamedElementAccessesAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Buffer.cs",
          PerformanceSources.CreateNamedIndexerPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildNamedArgumentIndexerPlan(
          context.RuleContext,
          context.FindIndexerParameterTypeSyntax("Buffer.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public int this[int index] => index;",
          plan.ReplacementIndexer.NormalizeWhitespace().ToFullString());
        Assert.Equal(
          new[]
          {
            "buffer[index: 1]",
            "buffer[index: frame]"
          },
          plan.AccessRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void DelegateMethodGroupPlan_RewritesMethodGroupTargetsAndDelegateInvocationsAcrossMultipleTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateMethodGroupPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegateMethodGroupPlan(
          context.RuleContext,
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate int Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
        Assert.Single(plan.MethodRewrites);
        Assert.Contains(
          "public static int Apply(int frame)",
          plan.MethodRewrites[0].ReplacementMethod.NormalizeWhitespace().ToFullString(),
          StringComparison.Ordinal);
        Assert.Equal(
          new[]
          {
            "handler(frame)",
            "handler.Invoke(frame)"
          },
          plan.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
        Assert.Empty(plan.LambdaRewrites);
    }

    [Fact]
    public void DelegateLambdaPlan_RewritesConvertedLambdaBindingsAcrossMultipleTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateLambdaPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegateLambdaPlan(
          context.RuleContext,
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate int Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
        Assert.Single(plan.LambdaRewrites);
        Assert.Equal(
          "(currentFrame) => currentFrame",
          plan.LambdaRewrites[0].Replacement.NormalizeWhitespace().ToFullString());
        Assert.Equal(
          new[] { "handler(frame)" },
          plan.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .ToArray());
        Assert.Empty(plan.MethodRewrites);
    }

    [Fact]
    public void DelegateInvocationChainPlan_RewritesInvocationChainsWithoutBindingRewrites()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateInvocationChainPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegateInvocationChainPlan(
          context.RuleContext,
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate int Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
        Assert.Empty(plan.MethodRewrites);
        Assert.Empty(plan.LambdaRewrites);
        Assert.Equal(
          new[]
          {
            "alias.Invoke(frame)",
            "handler(frame)"
          },
          plan.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void ExtensionReceiverPlan_RewritesReducedAndStaticExtensionInvocationsAcrossMultipleTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "InputExtensions.cs",
          PerformanceSources.CreateExtensionReceiverPlanFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildExtensionReceiverNonFirstParameterPlan(
          context.RuleContext,
          context.FindParameterTypeSyntax("InputExtensions.cs", "Score", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Contains(
          "public static int Score(this int value, int frame)",
          plan.ReplacementMethod.NormalizeWhitespace().ToFullString(),
          StringComparison.Ordinal);
        Assert.Equal(
          new[]
          {
            "1.Score(2)",
            "InputExtensions.Score(frame, 3)"
          },
          plan.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void DelegateParameterPlan_FailsWhenDelegateTypeIsStillReferencedByAnotherTypeSyntax()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateReferencedTypeFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegatePlan(
          context.RuleContext,
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void DelegateParameterPlan_SucceedsWhenCompilationContainsOnlyTheDelegateDeclaration()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateOnlyFiles());
        var analyzer = new DeleteClassParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegatePlan(
          context.RuleContext,
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate void Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
    }

    [Fact]
    public void CompilationScanCache_MaterializesOnlyRequestedSyntaxTrees()
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText("namespace Demo; public sealed class First { }", path: "First.cs"),
            CSharpSyntaxTree.ParseText("namespace Demo; public sealed class Second { }", path: "Second.cs"),
            CSharpSyntaxTree.ParseText("namespace Demo; public sealed class Third { }", path: "Third.cs")
        };
        var compilation = CreateCompilation(trees);
        var runtime = DeletionAnalysisRuntime.CreateDefault();
        var cache = GetDeleteClassCompilationScanCache(runtime, compilation);

        Assert.Equal(0, GetPrivateIntField(cache, "_materializedTreeCount"));

        var firstScan = GetTreeScan(cache, trees[0]);
        Assert.Equal(1, GetPrivateIntField(cache, "_materializedTreeCount"));

        var firstScanAgain = GetTreeScan(cache, trees[0]);
        Assert.Same(firstScan, firstScanAgain);
        Assert.Equal(1, GetPrivateIntField(cache, "_materializedTreeCount"));

        var secondScan = GetTreeScan(cache, trees[1]);
        Assert.NotNull(secondScan);
        Assert.Equal(2, GetPrivateIntField(cache, "_materializedTreeCount"));
    }

    [Fact]
    public void TreeScan_BuildsIndexesOnlyWhenQueried()
    {
        var tree = CSharpSyntaxTree.ParseText(
          PerformanceSources.TreeScanSource,
          path: "TreeScan.cs");
        var compilation = CreateCompilation(new[] { tree });
        var runtime = DeletionAnalysisRuntime.CreateDefault();
        var cache = GetDeleteClassCompilationScanCache(runtime, compilation);
        var scan = GetTreeScan(cache, tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var delegateSymbol = (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(
          root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Single(),
          CancellationToken.None)!;
        var invokeMethod = delegateSymbol.DelegateInvokeMethod!;
        var playerInputSymbol = (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(
          root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "PlayerInput"),
          CancellationToken.None)!;

        AssertIndexBuildCounts(
          scan,
          invocation: 0,
          mappedInvocation: 0,
          elementAccess: 0,
          expression: 0,
          typeSyntax: 0);

        var typeBindings = GetTypeSyntaxBindings(scan, playerInputSymbol);
        Assert.Single(typeBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 0,
          mappedInvocation: 0,
          elementAccess: 0,
          expression: 0,
          typeSyntax: 1);

        var invocationBindings = GetInvocationBindings(scan, invokeMethod);
        Assert.Single(invocationBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 1,
          mappedInvocation: 0,
          elementAccess: 0,
          expression: 0,
          typeSyntax: 1);

        var expressionBindings = GetExpressionBindings(scan, delegateSymbol);
        Assert.NotEmpty(expressionBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 1,
          mappedInvocation: 0,
          elementAccess: 0,
          expression: 1,
          typeSyntax: 1);

        var mappedInvocationBindings = GetMappedInvocationBindings(scan, invokeMethod);
        Assert.Single(mappedInvocationBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 1,
          mappedInvocation: 1,
          elementAccess: 0,
          expression: 1,
          typeSyntax: 1);
    }

    [Fact]
    public void AnalyzeFromArgs_RemovesUnusedUsingsAcrossMultipleFilesDuringSharedCleanupPass()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "cleanup-multi-file-usings");
        Directory.CreateDirectory(projectDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var firstConsumerPath = Path.Combine(projectDirectory, "GameA.cs");
        var secondConsumerPath = Path.Combine(projectDirectory, "GameB.cs");
        File.WriteAllText(
          classFilePath,
          PerformanceSources.CleanupPlayerInputSource);
        File.WriteAllText(
          firstConsumerPath,
          PerformanceSources.CleanupFirstConsumerSource);
        File.WriteAllText(
          secondConsumerPath,
          PerformanceSources.CleanupSecondConsumerSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var firstConsumerSource = File.ReadAllText(firstConsumerPath);
        var secondConsumerSource = File.ReadAllText(secondConsumerPath);
        Assert.DoesNotContain("using Demo.Input;", firstConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using System;", firstConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using Demo.Input;", secondConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using System;", secondConsumerSource, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_RemovesEmptyNamespacesAcrossMultipleFilesDuringSharedCleanupPass()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "cleanup-multi-file-namespaces");
        Directory.CreateDirectory(projectDirectory);
        var firstFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var secondFilePath = Path.Combine(projectDirectory, "OtherPlayerInput.cs");
        File.WriteAllText(
          firstFilePath,
          PerformanceSources.FirstEmptyNamespaceSource);
        File.WriteAllText(
          secondFilePath,
          PerformanceSources.SecondEmptyNamespaceSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var firstSource = File.ReadAllText(firstFilePath);
        var secondSource = File.ReadAllText(secondFilePath);
        Assert.True(string.IsNullOrWhiteSpace(firstSource));
        Assert.True(string.IsNullOrWhiteSpace(secondSource));
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public async Task AnalyzeFromArgs_ForDirectoryDeleteClass_RecordsDiffWritePerformanceAcrossDopLevels()
    {
        var sourceDirectory = WriteDiffPerformanceSources();

        var serial = await MeasureDiffWritePerformanceAsync(sourceDirectory, 1);
        var parallel = await MeasureDiffWritePerformanceAsync(sourceDirectory, 16);

        Assert.Equal(DiffPerformanceConsumerCount + 1, serial.DiffFiles.Count);
        Assert.Equal(serial.DiffFiles.Keys, parallel.DiffFiles.Keys);
        foreach (var relativePath in serial.DiffFiles.Keys)
        {
            Assert.Equal(serial.DiffFiles[relativePath], parallel.DiffFiles[relativePath]);
        }

        Assert.InRange(serial.WriteElapsedMilliseconds, 0, serial.LifecycleElapsedMilliseconds);
        Assert.InRange(parallel.WriteElapsedMilliseconds, 0, parallel.LifecycleElapsedMilliseconds);
        _output.WriteLine(
          $"directory-diff-write-performance files={serial.DiffFiles.Count};" +
          $"dop1WriteMs={serial.WriteElapsedMilliseconds};" +
          $"dop16WriteMs={parallel.WriteElapsedMilliseconds};" +
          $"dop1LifecycleMs={serial.LifecycleElapsedMilliseconds};" +
          $"dop16LifecycleMs={parallel.LifecycleElapsedMilliseconds}");
    }

    [Fact]
    public async Task AnalyzeFromArgs_ForMultiFileDirectory_RecordsCompleteTextLogsAcrossDopLevels()
    {
        var sourceDirectory = WriteDirectoryIoPerformanceSources();

        var serialWithoutLogs = await MeasureDirectoryIoPerformanceAsync(
          sourceDirectory,
          maxDegreeOfParallelism: 1,
          writeLogs: false);
        var serialWithLogs = await MeasureDirectoryIoPerformanceAsync(
          sourceDirectory,
          maxDegreeOfParallelism: 1,
          writeLogs: true);
        var parallelWithoutLogs = await MeasureDirectoryIoPerformanceAsync(
          sourceDirectory,
          maxDegreeOfParallelism: 16,
          writeLogs: false);
        var parallelWithLogs = await MeasureDirectoryIoPerformanceAsync(
          sourceDirectory,
          maxDegreeOfParallelism: 16,
          writeLogs: true);

        var measurements = new[]
        {
          serialWithoutLogs,
          serialWithLogs,
          parallelWithoutLogs,
          parallelWithLogs
        };

        Assert.All(measurements, measurement =>
          Assert.Equal(DiffPerformanceConsumerCount + 1, measurement.AnalyzedFileCount));
        Assert.Equal(serialWithoutLogs.ResultSnapshot, serialWithLogs.ResultSnapshot);
        Assert.Equal(serialWithoutLogs.ResultSnapshot, parallelWithoutLogs.ResultSnapshot);
        Assert.Equal(serialWithoutLogs.ResultSnapshot, parallelWithLogs.ResultSnapshot);
        Assert.Equal(DiffPerformanceConsumerCount + 1, serialWithLogs.AnalysisFileCompletedRecordCount);
        Assert.Equal(DiffPerformanceConsumerCount + 1, parallelWithLogs.AnalysisFileCompletedRecordCount);
        Assert.True(serialWithLogs.RuntimeCompleted);
        Assert.True(parallelWithLogs.RuntimeCompleted);
        Assert.True(serialWithLogs.TotalLogBytes > 0);
        Assert.True(parallelWithLogs.TotalLogBytes > 0);
        _output.WriteLine(
          $"directory-io-log-performance files={serialWithLogs.AnalyzedFileCount};" +
          $"dop1NoLogMs={serialWithoutLogs.ElapsedMilliseconds};" +
          $"dop1LogMs={serialWithLogs.ElapsedMilliseconds};" +
          $"dop16NoLogMs={parallelWithoutLogs.ElapsedMilliseconds};" +
          $"dop16LogMs={parallelWithLogs.ElapsedMilliseconds};" +
          $"dop1LogBytes={serialWithLogs.TotalLogBytes};" +
          $"dop16LogBytes={parallelWithLogs.TotalLogBytes}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<DiffWritePerformanceMeasurement> MeasureDiffWritePerformanceAsync(
      string sourceDirectory,
      int maxDegreeOfParallelism)
    {
        var diffRootPath = Path.Combine(_tempDirectory, $"diff-performance-dop-{maxDegreeOfParallelism}");
        var analysisLogPath = Path.Combine(_tempDirectory, $"diff-performance-dop-{maxDegreeOfParallelism}.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourceDirectory,
            "--delete-class",
            "PlayerInput",
            "--max-degree-of-parallelism",
            maxDegreeOfParallelism.ToString(),
            "--diff-out",
            diffRootPath,
            "--analysis-log",
            analysisLogPath,
            "--log-profile",
            "benchmark"
        });

        var summaryLine = File.ReadLines(analysisLogPath)
          .Single(line => line.Contains("cat=diff evt=summary", StringComparison.Ordinal));
        return new DiffWritePerformanceMeasurement(
          ReadLogField(summaryLine, "writeElapsedMs"),
          ReadLogField(summaryLine, "elapsedMs"),
          Directory.EnumerateFiles(diffRootPath, "*.rewrite.diff", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(diffRootPath, path), StringComparer.Ordinal)
            .ToDictionary(
              path => Path.GetRelativePath(diffRootPath, path),
              File.ReadAllBytes,
              StringComparer.Ordinal));
    }

    private async Task<DirectoryIoPerformanceMeasurement> MeasureDirectoryIoPerformanceAsync(
      string sourceDirectory,
      int maxDegreeOfParallelism,
      bool writeLogs)
    {
        var runName = $"directory-io-dop-{maxDegreeOfParallelism}-{(writeLogs ? "logs" : "none")}";
        var runtimeLogPath = Path.Combine(_tempDirectory, $"{runName}-runtime.log");
        var analysisLogPath = Path.Combine(_tempDirectory, $"{runName}-analysis.log");
        var arguments = new List<string>
        {
          sourceDirectory,
          "--delete-class",
          "PlayerInput",
          "--max-degree-of-parallelism",
          maxDegreeOfParallelism.ToString(),
          "--skip-rewrite",
          "--no-diff"
        };
        if (writeLogs)
        {
            arguments.Add("--runtime-log");
            arguments.Add(runtimeLogPath);
            arguments.Add("--analysis-log");
            arguments.Add(analysisLogPath);
            arguments.Add("--log-profile");
            arguments.Add("benchmark");
        }

        var stopwatch = Stopwatch.StartNew();
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());
        var result = await host.AnalyzeFromArgsAsync(arguments.ToArray());
        stopwatch.Stop();

        var runtimeLogLines = writeLogs
          ? File.ReadLines(runtimeLogPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
          : Array.Empty<string>();
        var analysisLogLines = writeLogs
          ? File.ReadLines(analysisLogPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
          : Array.Empty<string>();
        return new DirectoryIoPerformanceMeasurement(
          result.Stats?.AnalyzedFileCount ?? 0,
          CreateResultSnapshot(result),
          stopwatch.ElapsedMilliseconds,
          analysisLogLines.Count(line => line.Contains("cat=file evt=completed", StringComparison.Ordinal)),
          runtimeLogLines.Any(line => line.Contains("cat=run evt=completed", StringComparison.Ordinal) &&
            line.Contains("status=completed", StringComparison.Ordinal)),
          writeLogs ? new FileInfo(runtimeLogPath).Length + new FileInfo(analysisLogPath).Length : 0);
    }

    private string WriteDirectoryIoPerformanceSources()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "directory-io-performance-input");
        var consumerDirectory = Path.Combine(sourceDirectory, "Consumers");
        Directory.CreateDirectory(consumerDirectory);
        File.WriteAllText(
          Path.Combine(sourceDirectory, "PlayerInput.cs"),
          DirectoryDeleteClassSources.PlayerInputEnabledSource);

        for (var consumerIndex = 0; consumerIndex < DiffPerformanceConsumerCount; consumerIndex++)
        {
            var source = new StringBuilder();
            source.AppendLine("namespace Demo;");
            source.AppendLine();
            source.AppendLine($"public sealed class DirectoryConsumer{consumerIndex:D2}");
            source.AppendLine("{");
            source.AppendLine("  public int Run()");
            source.AppendLine("  {");
            source.AppendLine("    return PlayerInput.Enabled ? 1 : 0;");
            source.AppendLine("  }");
            source.AppendLine("}");
            File.WriteAllText(
              Path.Combine(consumerDirectory, $"DirectoryConsumer{consumerIndex:D2}.cs"),
              source.ToString());
        }

        return sourceDirectory;
    }

    private static string CreateResultSnapshot(PrototypeAnalysisResult result)
    {
        var decisions = result.Decisions
          .Select(decision => string.Join(
            "|",
            decision.FinalNode.SyntaxTree.FilePath,
            decision.FinalNode.SpanStart,
            decision.FinalNode.Span.Length,
            decision.Action,
            decision.ReplacementNode?.ToFullString() ?? string.Empty))
          .OrderBy(value => value, StringComparer.Ordinal);
        var edits = result.Edits
          .Select(edit => string.Join(
            "|",
            edit.FilePath,
            edit.Span.Start,
            edit.Span.Length,
            edit.OriginalText,
            edit.ReplacementText))
          .OrderBy(value => value, StringComparer.Ordinal);
        return string.Join("\n", decisions.Concat(edits));
    }

    private string WriteDiffPerformanceSources()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "diff-performance-input");
        var consumerDirectory = Path.Combine(sourceDirectory, "Consumers");
        Directory.CreateDirectory(consumerDirectory);
        File.WriteAllText(
          Path.Combine(sourceDirectory, "PlayerInput.cs"),
          DirectoryDeleteClassSources.PlayerInputEnabledSource);

        for (var consumerIndex = 0; consumerIndex < DiffPerformanceConsumerCount; consumerIndex++)
        {
            var source = new StringBuilder();
            source.AppendLine("namespace Demo;");
            source.AppendLine();
            source.AppendLine($"public sealed class Consumer{consumerIndex:D2}");
            source.AppendLine("{");
            source.AppendLine("  public int Run()");
            source.AppendLine("  {");
            source.AppendLine("    var total = 0;");
            for (var referenceIndex = 0; referenceIndex < DiffPerformanceReferencesPerConsumer; referenceIndex++)
            {
                source.AppendLine("    total += PlayerInput.Enabled ? 1 : 0;");
            }

            source.AppendLine("    return total;");
            source.AppendLine("  }");
            source.AppendLine("}");
            File.WriteAllText(
              Path.Combine(consumerDirectory, $"Consumer{consumerIndex:D2}.cs"),
              source.ToString());
        }

        return sourceDirectory;
    }

    private static long ReadLogField(string line, string fieldName)
    {
        var prefix = $"{fieldName}=";
        var field = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Single(token => token.StartsWith(prefix, StringComparison.Ordinal));
        return long.Parse(field[prefix.Length..]);
    }

    private static AnalyzerTestContext CreateDeleteClassContext(
      string declarationFilePath,
      params (string FilePath, string Source)[] files)
    {
        var trees = files.ToDictionary(
          file => file.FilePath,
          file => CSharpSyntaxTree.ParseText(file.Source, path: file.FilePath),
          StringComparer.Ordinal);
        var compilation = CreateCompilation(trees.Values);
        var declarationTree = trees[declarationFilePath];
        var declarationRoot = declarationTree.GetRoot();
        var declarationSemanticModel = compilation.GetSemanticModel(declarationTree);
        var declarationSource = files
          .Single(file => string.Equals(file.FilePath, declarationFilePath, StringComparison.Ordinal))
          .Source;
        var graph = new RoslynCpgBuilder().BuildFromSource(declarationSource, declarationFilePath);
        var ruleContext = new RuleContext(
          new CpgAnalysisContext(graph, declarationSemanticModel, declarationRoot),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var roots = trees.ToDictionary(
          pair => pair.Key,
          pair => pair.Value.GetRoot(),
          StringComparer.Ordinal);
        return new AnalyzerTestContext(ruleContext, roots);
    }

    private static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
    {
        return CSharpCompilation.Create(
          "PerformanceOptimizationRegressionTests",
          trees,
          new[]
          {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
          },
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static object GetDeleteClassCompilationScanCache(DeletionAnalysisRuntime runtime, Compilation compilation)
    {
        return GetCompilationCache(
          runtime,
          compilation,
          static currentCompilation =>
          {
              var cacheType = typeof(DeleteClassParameterShrinkAnalyzer).GetNestedType(
                "CompilationScanCache",
                BindingFlags.NonPublic)!;
              return Activator.CreateInstance(cacheType, currentCompilation)!;
          });
    }

    private static object GetTreeScan(object cache, SyntaxTree tree)
    {
        return cache.GetType()
          .GetMethod("GetTreeScan", BindingFlags.Instance | BindingFlags.Public)!
          .Invoke(cache, new object[] { tree })!;
    }

    private static IReadOnlyList<object> GetInvocationBindings(object scan, IMethodSymbol methodSymbol)
    {
        return InvokeScanListMethod(scan, "GetInvocationBindings", methodSymbol);
    }

    private static IReadOnlyList<object> GetMappedInvocationBindings(object scan, IMethodSymbol methodSymbol)
    {
        return InvokeScanListMethod(scan, "GetMappedInvocationBindings", methodSymbol);
    }

    private static IReadOnlyList<object> GetExpressionBindings(object scan, INamedTypeSymbol delegateSymbol)
    {
        return InvokeScanListMethod(scan, "GetExpressionBindings", delegateSymbol);
    }

    private static IReadOnlyList<object> GetTypeSyntaxBindings(object scan, INamedTypeSymbol targetSymbol)
    {
        return InvokeScanListMethod(scan, "GetTypeSyntaxBindings", targetSymbol);
    }

    private static IReadOnlyList<object> InvokeScanListMethod(object scan, string methodName, object argument)
    {
        return ((System.Collections.IEnumerable)scan.GetType()
          .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!
          .Invoke(scan, new[] { argument })!)
          .Cast<object>()
          .ToList();
    }

    private static int GetPrivateIntField(object instance, string fieldName)
    {
        return (int)instance.GetType()
          .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
          .GetValue(instance)!;
    }

    private static void AssertIndexBuildCounts(
      object scan,
      int invocation,
      int mappedInvocation,
      int elementAccess,
      int expression,
      int typeSyntax)
    {
        Assert.Equal(invocation, GetPrivateIntField(scan, "_invocationIndexBuildCount"));
        Assert.Equal(mappedInvocation, GetPrivateIntField(scan, "_mappedInvocationIndexBuildCount"));
        Assert.Equal(elementAccess, GetPrivateIntField(scan, "_elementAccessIndexBuildCount"));
        Assert.Equal(expression, GetPrivateIntField(scan, "_expressionIndexBuildCount"));
        Assert.Equal(typeSyntax, GetPrivateIntField(scan, "_typeSyntaxIndexBuildCount"));
    }

    private static TCache GetCompilationCache<TCache>(
      DeletionAnalysisRuntime runtime,
      Compilation compilation,
      Func<Compilation, TCache> factory)
    {
        var method = typeof(DeletionAnalysisRuntime)
          .GetMethod("GetOrCreateCompilationCache", BindingFlags.Instance | BindingFlags.NonPublic)!
          .MakeGenericMethod(typeof(TCache));
        return (TCache)method.Invoke(runtime, new object[] { compilation, factory })!;
    }

    private static MarkPerformanceMeasurement MeasureMarkAnalysis(string source, int maxDegreeOfParallelism)
    {
        var runtime = new DeletionAnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: maxDegreeOfParallelism,
            EnableGroupParallelism: maxDegreeOfParallelism > 1),
          new DeletionAnalysisEpoch(0, 0, 0));
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target-name"] = "s, other, s"
        };
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var result = new DeletionApplicationService(RuleRegistry.CreateDefaultRules()).Analyze(
          source,
          "mark-performance.cs",
          options,
          runtime);
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var snapshot = string.Join(
          "|",
          result.SeedMarks.Select(mark => $"seed:{mark.RuleId}:{mark.SyntaxNode.Span}")
            .Concat(result.PropagatedMarks.Select(mark => $"propagate:{mark.RuleId}:{mark.Mark.SyntaxNode.Span}"))
            .Concat(result.LiftedMarks.Select(mark => $"lift:{mark.RuleId}:{mark.Mark.SyntaxNode.Span}"))
            .Concat(result.Decisions.Select(decision => $"decision:{decision}"))
            .Append($"rewrite:{result.RewrittenSource}"));
        return new MarkPerformanceMeasurement(
          result.Timings!.MarkMilliseconds,
          allocatedBytes,
          result.MarkAnalysisTelemetry!,
          snapshot);
    }

    private sealed record DiffWritePerformanceMeasurement(
      long WriteElapsedMilliseconds,
      long LifecycleElapsedMilliseconds,
      IReadOnlyDictionary<string, byte[]> DiffFiles);

    private sealed record MarkPerformanceMeasurement(
      long MarkMilliseconds,
      long AllocatedBytes,
      MarkAnalysisTelemetry MarkTelemetry,
      string Snapshot);

    private sealed record DirectoryIoPerformanceMeasurement(
      int AnalyzedFileCount,
      string ResultSnapshot,
      long ElapsedMilliseconds,
      int AnalysisFileCompletedRecordCount,
      bool RuntimeCompleted,
      long TotalLogBytes);

    private sealed class AnalyzerTestContext
    {
        private readonly IReadOnlyDictionary<string, SyntaxNode> _rootsByPath;

        public AnalyzerTestContext(
          RuleContext ruleContext,
          IReadOnlyDictionary<string, SyntaxNode> rootsByPath)
        {
            RuleContext = ruleContext;
            _rootsByPath = rootsByPath;
        }

        public RuleContext RuleContext { get; }

        public TypeSyntax FindParameterTypeSyntax(
          string filePath,
          string methodName,
          string parameterName)
        {
            return _rootsByPath[filePath]
              .DescendantNodes()
              .OfType<MethodDeclarationSyntax>()
              .Single(method => string.Equals(method.Identifier.ValueText, methodName, StringComparison.Ordinal))
              .ParameterList.Parameters
              .Single(parameter => string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
              .Type!;
        }

        public TypeSyntax FindIndexerParameterTypeSyntax(string filePath, string parameterName)
        {
            return _rootsByPath[filePath]
              .DescendantNodes()
              .OfType<IndexerDeclarationSyntax>()
              .Single()
              .ParameterList.Parameters
              .Single(parameter => string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
              .Type!;
        }

        public TypeSyntax FindDelegateParameterTypeSyntax(string filePath, string parameterName)
        {
            return _rootsByPath[filePath]
              .DescendantNodes()
              .OfType<DelegateDeclarationSyntax>()
              .Single()
              .ParameterList.Parameters
              .Single(parameter => string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
              .Type!;
        }
    }
}
