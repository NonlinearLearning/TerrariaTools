using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Builder;
using NLISSN;
using NLISSN.Core.Analysis;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.DirectoryFixtures;
using RoslynPrototype.Tests.TestCodeSet.Performance;
using NLISSN.Rules;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

public sealed class PerformanceOptimizationRegressionTests : IDisposable
{
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
        Assert.All(parallelSamples, sample => Assert.True(sample.RuleNodeCount > 0));
        _output.WriteLine(
          $"Mark samples ms={string.Join(",", parallelSamples.Select(sample => sample.MarkMilliseconds))}; " +
          $"allocated={string.Join(",", parallelSamples.Select(sample => sample.AllocatedBytes))}; " +
          $"rule-nodes={string.Join(",", parallelSamples.Select(sample => sample.RuleNodeCount))}");
    }

    [Fact]
    public void NamedArgumentMethodPlan_RewritesNamedCallsitesAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeclarationContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateNamedArgumentMethodPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildNamedArgumentMethodPlan(
          context.RuleContext.CreateSemanticRuleContext(),
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
        var context = CreateDeclarationContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateOptionalParameterMethodPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildOptionalParameterMethodPlan(
          context.RuleContext.CreateSemanticRuleContext(),
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
        var context = CreateDeclarationContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateImplicitParamsMethodPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildParamsMethodPlan(
          context.RuleContext.CreateSemanticRuleContext(),
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
        var context = CreateDeclarationContext(
          declarationFilePath: "Game.cs",
          PerformanceSources.CreateExplicitParamsMethodPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildParamsMethodPlan(
          context.RuleContext.CreateSemanticRuleContext(),
          context.FindParameterTypeSyntax("Game.cs", "Apply", "inputs"),
          out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void NamedIndexerPlan_RewritesNamedElementAccessesAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeclarationContext(
          declarationFilePath: "Buffer.cs",
          PerformanceSources.CreateNamedIndexerPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildNamedArgumentIndexerPlan(
          context.RuleContext.CreateSemanticRuleContext(),
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
        var context = CreateDeclarationContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateMethodGroupPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegateMethodGroupPlan(
          context.RuleContext.CreateSemanticRuleContext(),
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate int Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
        Assert.Single(plan.Usage.MethodRewrites);
        Assert.Contains(
          "public static int Apply(int frame)",
          plan.Usage.MethodRewrites[0].ReplacementMethod.NormalizeWhitespace().ToFullString(),
          StringComparison.Ordinal);
        Assert.Equal(
          new[]
          {
            "handler(frame)",
            "handler.Invoke(frame)"
          },
          plan.Usage.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
        Assert.Empty(plan.Usage.LambdaRewrites);
    }

    [Fact]
    public void DelegateLambdaPlan_RewritesConvertedLambdaBindingsAcrossMultipleTrees()
    {
        var context = CreateDeclarationContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateLambdaPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegateLambdaPlan(
          context.RuleContext.CreateSemanticRuleContext(),
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate int Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
        Assert.Single(plan.Usage.LambdaRewrites);
        Assert.Equal(
          "(currentFrame) => currentFrame",
          plan.Usage.LambdaRewrites[0].Replacement.NormalizeWhitespace().ToFullString());
        Assert.Equal(
          new[] { "handler(frame)" },
          plan.Usage.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .ToArray());
        Assert.Empty(plan.Usage.MethodRewrites);
    }

    [Fact]
    public void DelegateInvocationChainPlan_RewritesInvocationChainsWithoutBindingRewrites()
    {
        var context = CreateDeclarationContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateInvocationChainPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegateInvocationChainPlan(
          context.RuleContext.CreateSemanticRuleContext(),
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out var plan);

        Assert.True(succeeded);
        Assert.Equal(
          "public delegate int Handler(int frame);",
          plan.ReplacementDelegate.NormalizeWhitespace().ToFullString());
        Assert.Empty(plan.Usage.MethodRewrites);
        Assert.Empty(plan.Usage.LambdaRewrites);
        Assert.Equal(
          new[]
          {
            "alias.Invoke(frame)",
            "handler(frame)"
          },
          plan.Usage.InvocationRewrites
            .Select(rewrite => rewrite.Replacement.NormalizeWhitespace().ToFullString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void ExtensionReceiverPlan_RewritesReducedAndStaticExtensionInvocationsAcrossMultipleTrees()
    {
        var context = CreateDeclarationContext(
          declarationFilePath: "InputExtensions.cs",
          PerformanceSources.CreateExtensionReceiverPlanFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildExtensionReceiverNonFirstParameterPlan(
          context.RuleContext.CreateSemanticRuleContext(),
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
        var context = CreateDeclarationContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateReferencedTypeFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegatePlan(
          context.RuleContext.CreateSemanticRuleContext(),
          context.FindDelegateParameterTypeSyntax("Handler.cs", "input"),
          out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void DelegateParameterPlan_SucceedsWhenCompilationContainsOnlyTheDelegateDeclaration()
    {
        var context = CreateDeclarationContext(
          declarationFilePath: "Handler.cs",
          PerformanceSources.CreateDelegateOnlyFiles());
        var analyzer = new ParameterShrinkAnalyzer();

        var succeeded = analyzer.TryBuildDelegatePlan(
          context.RuleContext.CreateSemanticRuleContext(),
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
        var runtime =  AnalysisRuntime.CreateDefault();
        var cache = GetDeclarationCompilationScanCache(runtime, compilation);

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
        var runtime =  AnalysisRuntime.CreateDefault();
        var cache = GetDeclarationCompilationScanCache(runtime, compilation);
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
          elementAccess: 0,
          expression: 0,
          typeSyntax: 0);

        var typeBindings = GetTypeSyntaxBindings(scan, playerInputSymbol);
        Assert.Single(typeBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 0,
          elementAccess: 0,
          expression: 0,
          typeSyntax: 1);

        var invocationBindings = GetInvocationBindings(scan, invokeMethod);
        Assert.Single(invocationBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 1,
          elementAccess: 0,
          expression: 0,
          typeSyntax: 1);

        var expressionBindings = GetExpressionBindings(scan, delegateSymbol);
        Assert.NotEmpty(expressionBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 1,
          elementAccess: 0,
          expression: 1,
          typeSyntax: 1);

        var mappedInvocationBindings = GetMappedInvocationBindings(scan, invokeMethod);
        Assert.Single(mappedInvocationBindings);
        AssertIndexBuildCounts(
          scan,
          invocation: 1,
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
        var commandHost = new CommandHost(RuleRegistry.CreateDefaultRules());

        var result = commandHost.AnalyzeFromArgs(new[]
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
        var commandHost = new CommandHost(RuleRegistry.CreateDefaultRules());

        var result = commandHost.AnalyzeFromArgs(new[]
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

    private static AnalyzerTestContext CreateDeclarationContext(string declarationFilePath, params (string FilePath, string Source)[] files)
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
        var graph = new NLCPGBuilder().BuildFromSource(declarationSource, declarationFilePath);
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

    private static object GetDeclarationCompilationScanCache( AnalysisRuntime runtime, Compilation compilation)
    {
        return GetCompilationCache(
          runtime,
          compilation,
          static currentCompilation =>
          {
              var cacheType = typeof(ParameterShrinkAnalyzer).GetNestedType(
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

    private static void AssertIndexBuildCounts(object scan, int invocation, int elementAccess, int expression, int typeSyntax)
    {
        Assert.Equal(invocation, GetPrivateIntField(scan, "_invocationIndexBuildCount"));
        Assert.Equal(elementAccess, GetPrivateIntField(scan, "_elementAccessIndexBuildCount"));
        Assert.Equal(expression, GetPrivateIntField(scan, "_expressionIndexBuildCount"));
        Assert.Equal(typeSyntax, GetPrivateIntField(scan, "_typeSyntaxIndexBuildCount"));
    }

    private static TCache GetCompilationCache<TCache>( AnalysisRuntime runtime, Compilation compilation, Func<Compilation, TCache> factory)
    {
        var method = typeof( AnalysisRuntime)
          .GetMethod(
            "GetOrCreateCompilationCache",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
          .MakeGenericMethod(typeof(TCache));
        return (TCache)method.Invoke(runtime, new object[] { compilation, factory })!;
    }

    private static MarkPerformanceMeasurement MeasureMarkAnalysis(string source, int maxDegreeOfParallelism)
    {
        var runtime = new  AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: maxDegreeOfParallelism,
            EnableGroupParallelism: maxDegreeOfParallelism > 1),
          new  AnalysisEpoch(0, 0, 0));
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target-name"] = "s, other, s"
        };
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        var result = new  ApplicationService(RuleRegistry.CreateDefaultRules()).Analyze(
          source,
          "mark-performance.cs",
          options,
          runtime);
        stopwatch.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var ruleGraphTelemetry = Assert.IsAssignableFrom<IReadOnlyList<RuleGraphNodeTelemetry>>(result.RuleGraphTelemetry);
        var snapshot = string.Join(
          "|",
          result.SeedMarks.Select(mark => $"seed:{mark.RuleId}:{mark.SyntaxNode.Span}")
            .Concat(result.PropagatedMarks.Select(mark => $"propagate:{mark.RuleId}:{mark.Mark.SyntaxNode.Span}"))
            .Concat(result.LiftedMarks.Select(mark => $"lift:{mark.RuleId}:{mark.Mark.SyntaxNode.Span}"))
            .Concat(result.Decisions.Select(decision => $"decision:{decision}"))
            .Append($"rewrite:{result.RewrittenSource}"));
        return new MarkPerformanceMeasurement(
          stopwatch.ElapsedMilliseconds,
          allocatedBytes,
          snapshot,
          ruleGraphTelemetry.Count);
    }

    private sealed record MarkPerformanceMeasurement(
      long MarkMilliseconds,
      long AllocatedBytes,
      string Snapshot,
      int RuleNodeCount);

    private sealed class AnalyzerTestContext
    {
        private readonly IReadOnlyDictionary<string, SyntaxNode> _rootsByPath;

        public AnalyzerTestContext(RuleContext ruleContext, IReadOnlyDictionary<string, SyntaxNode> rootsByPath)
        {
            RuleContext = ruleContext;
            _rootsByPath = rootsByPath;
        }

        public RuleContext RuleContext { get; }

        public TypeSyntax FindParameterTypeSyntax(string filePath, string methodName, string parameterName)
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
