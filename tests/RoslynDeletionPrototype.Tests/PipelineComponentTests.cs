using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Analysis;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Rewrite;
using Rules;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PipelineComponentTests : IDisposable
{
    private readonly string _tempDirectory;

    public PipelineComponentTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void MarkingEngine_Run_DeduplicatesSameRuleAndSyntaxSpan()
    {
        var source = SObjectExpressionSources.MarkingDedupSource;

        var (context, root) = CreateContext(source, "s");
        var engine = new MarkingEngine();
        var rules = new RuleDefinition[] { new DuplicateSeedRule() };

        var marks = engine.Run(context, root, rules);

        var mark = Assert.Single(marks);
        Assert.NotNull(mark.Annotation);
        Assert.NotNull(mark.PrimaryGraphNode);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)mark.SyntaxNode.RawKind);
    }

    [Fact]
    public void PropagationEngine_Run_DeduplicatesSamePropagatedSpan()
    {
        var source = SObjectControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedRule = new DeleteSObjectExpressionRule();
        var seedMarks = new MarkingEngine().Run(context, root, new RuleDefinition[] { seedRule });
        var engine = new PropagationEngine();
        var rules = new RuleDefinition[] { new DuplicatePropagationRule() };

        var propagatedMarks = engine.Run(context, seedMarks, rules);

        var propagated = Assert.Single(propagatedMarks);
        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)propagated.Mark.SyntaxNode.RawKind);
        Assert.NotNull(propagated.Mark.PrimaryGraphNode);
    }

    [Fact]
    public void PrototypeRewriter_Rewrite_ReplacesExpressionsAndDeletesStatements()
    {
        var source = RewriteSources.ReplaceAndDeleteSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "rewrite-test.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var declaration = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
        var identifier = root.DescendantNodes().OfType<IdentifierNameSyntax>().First(node => node.Identifier.ValueText == "temp");
        var decisions = new[]
        {
            new RuleDecision(identifier, identifier, DecisionActionKind.Delete, "Replace identifier with default."),
            new RuleDecision(declaration, declaration, DecisionActionKind.Delete, "Delete declaration.")
        };
        var rewriter = new PrototypeRewriter();

        var result = rewriter.Rewrite(root, semanticModel, decisions);

        Assert.Equal(2, result.Edits.Count);
        Assert.Contains("default(int)", result.RewrittenSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var temp = value + 1;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("<deleted>", result.DiffText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_WritesDiffFileWhenInputProducesEdits()
    {
        var filePath = Path.Combine(_tempDirectory, "delete-s-object-sample.cs");
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[] { filePath, "--target-name", "s" });

        Assert.NotNull(result.DiffFilePath);
        Assert.True(File.Exists(result.DiffFilePath));
        var diffText = File.ReadAllText(result.DiffFilePath);
        Assert.Contains("+++ rewritten #1", diffText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_UsesDefaultSourceWhenInputPathIsMissing()
    {
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[] { "--target-name", "s" });

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.Equal(2, result.PropagatedMarks.Count);
        Assert.Equal(2, result.Edits.Count);
        Assert.Null(result.DiffFilePath);
        Assert.Contains("return offset;", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_DoesNotWriteDiffFileWhenAnalysisProducesNoEdits()
    {
        var filePath = Path.Combine(_tempDirectory, "no-edits-sample.cs");
        File.WriteAllText(filePath, MinimalSources.EmptyMainSource);
        var explicitDiffPath = Path.Combine(_tempDirectory, "no-edits.diff");
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "missing",
            "--diff-out",
            explicitDiffPath
        });

        Assert.Empty(result.SeedMarks);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(explicitDiffPath));
    }

    [Fact]
    public void FormatResult_IncludesCountsDecisionsAndRewrittenSource()
    {
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
        var result = application.AnalyzeFromArgs(new[] { "--target-name", "s" });

        var lines = application.FormatResult(result);

        Assert.Contains(lines, line => string.Equals(line, "SeedMarks: 2", StringComparison.Ordinal));
        Assert.Contains(lines, line => string.Equals(line, "PropagatedMarks: 2", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("SEED [", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("PROPAGATED [", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("DECISION Delete [", StringComparison.Ordinal));
        Assert.Contains(lines, line => string.Equals(line, "--- Rewritten Source ---", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("return offset;", StringComparison.Ordinal));
    }

    [Fact]
    public void PrototypeRewriter_Rewrite_WhenNoDecisionsKeepsSourceAndEmptyDiff()
    {
        var source = RewriteSources.NoDecisionSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "no-decisions.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var rewriter = new PrototypeRewriter();

        var result = rewriter.Rewrite(root, semanticModel, Array.Empty<RuleDecision>());

        Assert.Empty(result.Edits);
        Assert.Contains("public sealed class Sample", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("return value + 1;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Empty(result.DiffText);
    }

    [Fact]
    public void RuleRegistry_CreateDefaultRules_ReturnsStableRuleSet()
    {
        var rules = RuleRegistry.CreateDefaultRules();

        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, rule => string.Equals(rule.RuleId, "DEL-SOBJ-001", StringComparison.Ordinal));
        Assert.Contains(rules, rule => string.Equals(rule.RuleId, "DEL-DEAD-001", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static (RuleContext Context, SyntaxNode Root) CreateContext(string source, string? targetName = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "component-test.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "component-test.cs");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        return (new RuleContext(new CpgAnalysisContext(graph, semanticModel, root), options), root);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CSharpCompilation.Create(
          "PipelineComponentTests",
          new[] { tree },
          new[]
          {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
          });
    }

    private sealed class DuplicateSeedRule : RuleDefinition
    {
        public string RuleId { get; } = "TEST-DUP-SEED";

        public string Name { get; } = "Emit duplicated seed marks";

        public IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.SimpleMemberAccessExpression };

        public IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IfStatement };

        public IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
            Array.Empty<SyntaxKind>();

        public IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
            Array.Empty<SyntaxKind>();

        public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();
            yield return new MarkRecord(RuleId, memberAccess, null, null, "first");
            yield return new MarkRecord(RuleId, memberAccess, null, null, "second");
        }

        public IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            _ = seedMarks;
            return Enumerable.Empty<PropagatedMarkRecord>();
        }

        public IEnumerable<DecisionUnit> Propose(
            RuleContext context,
            IReadOnlyList<MarkRecord> seedMarks,
            IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = propagatedMarks;
            return Enumerable.Empty<DecisionUnit>();
        }
    }

    private sealed class DuplicatePropagationRule : RuleDefinition
    {
        public string RuleId { get; } = "DEL-SOBJ-001";

        public string Name { get; } = "Emit duplicated propagated marks";

        public IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            Array.Empty<SyntaxKind>();

        public IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IfStatement };

        public IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
            Array.Empty<SyntaxKind>();

        public IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
            Array.Empty<SyntaxKind>();

        public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            _ = root;
            return Enumerable.Empty<MarkRecord>();
        }

        public IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var ifStatement = context.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
            var source = Assert.Single(seedMarks);
            var propagatedMark = new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, ifStatement, null, null, "lift to if"),
              source,
              1);
            yield return propagatedMark;
            yield return propagatedMark;
        }

        public IEnumerable<DecisionUnit> Propose(
            RuleContext context,
            IReadOnlyList<MarkRecord> seedMarks,
            IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = propagatedMarks;
            return Enumerable.Empty<DecisionUnit>();
        }
    }
}
