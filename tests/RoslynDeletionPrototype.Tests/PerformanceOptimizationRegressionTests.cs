using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Analysis;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Rewrite;
using Rules;
using System.Reflection;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PerformanceOptimizationRegressionTests : IDisposable
{
    private readonly string _tempDirectory;

    public PerformanceOptimizationRegressionTests()
    {
        _tempDirectory = Path.Combine(
          Path.GetTempPath(),
          $"roslyn-prototype-optimization-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void NamedArgumentMethodPlan_RewritesNamedCallsitesAcrossMultipleSyntaxTrees()
    {
        var context = CreateDeleteClassContext(
          declarationFilePath: "Game.cs",
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Game.cs",
            """
            namespace Demo;

            public sealed class Game
            {
              public int Apply(PlayerInput input, int frame)
              {
                return frame;
              }
            }
            """),
          (
            "RunnerA.cs",
            """
            namespace Demo;

            public sealed class RunnerA
            {
              public int Run(Game game)
              {
                return game.Apply(input: null, frame: 1);
              }
            }
            """),
          (
            "RunnerB.cs",
            """
            namespace Demo;

            public sealed class RunnerB
            {
              public int Run(Game game, int frame)
              {
                return game.Apply(input: null, frame: frame);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Game.cs",
            """
            namespace Demo;

            public sealed class Game
            {
              public int Apply(int frame, PlayerInput input = null, int scale = 1)
              {
                return frame * scale;
              }
            }
            """),
          (
            "RunnerA.cs",
            """
            namespace Demo;

            public sealed class RunnerA
            {
              public int Run(Game game, int frame)
              {
                return game.Apply(frame);
              }
            }
            """),
          (
            "RunnerB.cs",
            """
            namespace Demo;

            public sealed class RunnerB
            {
              public int Run(Game game, int frame)
              {
                return game.Apply(frame, input: null, scale: 2) + game.Apply(frame, scale: 3);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Game.cs",
            """
            namespace Demo;

            public sealed class Game
            {
              public int Apply(int frame, params PlayerInput[] inputs)
              {
                return frame;
              }
            }
            """),
          (
            "RunnerA.cs",
            """
            namespace Demo;

            public sealed class RunnerA
            {
              public int Run(Game game)
              {
                return game.Apply(1);
              }
            }
            """),
          (
            "RunnerB.cs",
            """
            namespace Demo;

            public sealed class RunnerB
            {
              public int Run(Game game, int frame)
              {
                return game.Apply(frame);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Game.cs",
            """
            namespace Demo;

            public sealed class Game
            {
              public int Apply(int frame, params PlayerInput[] inputs)
              {
                return frame;
              }
            }
            """),
          (
            "Runner.cs",
            """
            namespace Demo;

            public sealed class Runner
            {
              public int Run(Game game)
              {
                return game.Apply(1, null);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Buffer.cs",
            """
            namespace Demo;

            public sealed class Buffer
            {
              public int this[int index, PlayerInput input] => index;
            }
            """),
          (
            "RunnerA.cs",
            """
            namespace Demo;

            public sealed class RunnerA
            {
              public int Run(Buffer buffer)
              {
                return buffer[index: 1, input: null];
              }
            }
            """),
          (
            "RunnerB.cs",
            """
            namespace Demo;

            public sealed class RunnerB
            {
              public int Run(Buffer buffer, int frame)
              {
                return buffer[index: frame, input: null];
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Handler.cs",
            """
            namespace Demo;

            public delegate int Handler(PlayerInput input, int frame);
            """),
          (
            "Targets.cs",
            """
            namespace Demo;

            public static class Targets
            {
              public static int Apply(PlayerInput input, int frame)
              {
                return frame;
              }
            }
            """),
          (
            "Runner.cs",
            """
            namespace Demo;

            public sealed class Runner
            {
              public int Run(int frame)
              {
                Handler handler = Targets.Apply;
                return handler(null, frame) + handler.Invoke(null, frame);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Handler.cs",
            """
            namespace Demo;

            public delegate int Handler(PlayerInput input, int frame);
            """),
          (
            "Runner.cs",
            """
            namespace Demo;

            public sealed class Runner
            {
              public int Run(int frame)
              {
                Handler handler = (input, currentFrame) => currentFrame;
                return handler(null, frame);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Handler.cs",
            """
            namespace Demo;

            public delegate int Handler(PlayerInput input, int frame);
            """),
          (
            "Runner.cs",
            """
            namespace Demo;

            public sealed class Runner
            {
              public int Run(Handler handler, int frame)
              {
                var alias = handler;
                return handler(null, frame) + alias.Invoke(null, frame);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "InputExtensions.cs",
            """
            namespace Demo;

            public static class InputExtensions
            {
              public static int Score(this int value, PlayerInput input, int frame)
              {
                return value + frame;
              }
            }
            """),
          (
            "RunnerA.cs",
            """
            namespace Demo;

            public sealed class RunnerA
            {
              public int Run()
              {
                return 1.Score(null, 2);
              }
            }
            """),
          (
            "RunnerB.cs",
            """
            namespace Demo;

            public sealed class RunnerB
            {
              public int Run(int frame)
              {
                return InputExtensions.Score(frame, null, 3);
              }
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Handler.cs",
            """
            namespace Demo;

            public delegate void Handler(PlayerInput input, int frame);
            """),
          (
            "Holder.cs",
            """
            namespace Demo;

            public sealed class Holder
            {
              private readonly Handler _handler;
            }
            """));
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
          (
            "PlayerInput.cs",
            """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """),
          (
            "Handler.cs",
            """
            namespace Demo;

            public delegate void Handler(PlayerInput input, int frame);
            """));
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
          """
          namespace Demo;

          public delegate int Handler(PlayerInput input, int frame);

          public sealed class PlayerInput
          {
          }

          public sealed class Runner
          {
            public int Run(Handler handler)
            {
              return handler(null, 1);
            }
          }
          """,
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
          """
          namespace Demo.Input;

          public static class PlayerInput
          {
            public static bool Enabled => true;
          }
          """);
        File.WriteAllText(
          firstConsumerPath,
          """
          using Demo.Input;
          using System;

          namespace Demo;

          public sealed class GameA
          {
            public void Run()
            {
              Console.WriteLine(PlayerInput.Enabled);
            }
          }
          """);
        File.WriteAllText(
          secondConsumerPath,
          """
          using Demo.Input;
          using System;

          namespace Demo;

          public sealed class GameB
          {
            public void Run()
            {
              Console.WriteLine(PlayerInput.Enabled);
            }
          }
          """);
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
          """
          namespace Demo.One
          {
            public sealed class PlayerInput
            {
            }
          }
          """);
        File.WriteAllText(
          secondFilePath,
          """
          namespace Demo.Two
          {
            public sealed class PlayerInput
            {
            }
          }
          """);
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
