using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Analysis;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLISSN.Application;
using NLISSN.Hosting;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Rules;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DecisionEvidenceTests
{
  [Fact]
  public void Analyze_WhenDecisionIsProduced_ConnectsDecisionRootToSeedMark()
  {
    var application = new ApplicationService(
      RuleRegistry.CreateDefaultRules(enableUnreachableMethodDeletion: true));

    var result = application.Analyze(
      ReachabilitySources.ReachabilityIgnoresConfiguredMethodNamesSource,
      "decision-evidence.cs",
      new Dictionary<string, string>());

    var evidence = Assert.IsType<AnalysisEvidenceGraph>(result.Evidence);
    var decision = Assert.Single(result.Decisions);
    Assert.NotNull(decision.Evidence);
    Assert.Contains(evidence.Nodes, node => node.Id == decision.EvidenceRootId &&
      node.Kind == AnalysisEvidenceKind.Decision);
    Assert.Contains(evidence.Nodes, node => node.Kind == AnalysisEvidenceKind.SeedMark);
    Assert.True(HasSeedPath(decision.EvidenceRootId!, evidence));
  }

  [Fact]
  public void Analyze_WhenDopChanges_ProducesIdenticalEvidenceJson()
  {
    var application = new ApplicationService(
      RuleRegistry.CreateDefaultRules(enableUnreachableMethodDeletion: true));

    var sequential = application.Analyze(
      ReachabilitySources.ReachabilityIgnoresConfiguredMethodNamesSource,
      "decision-evidence-dop.cs",
      CreateOptions(1));
    var parallel = application.Analyze(
      ReachabilitySources.ReachabilityIgnoresConfiguredMethodNamesSource,
      "decision-evidence-dop.cs",
      CreateOptions(16));

    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    Assert.Equal(
      JsonSerializer.Serialize(sequential.Evidence, options),
      JsonSerializer.Serialize(parallel.Evidence, options));
  }

  [Fact]
  public void Complete_WhenBudgetIsExceeded_ReportsTruncation()
  {
    var tree = CSharpSyntaxTree.ParseText("class C { void M() { int value = 1; } }");
    var nodes = tree.GetRoot().DescendantNodes().Take(2).ToArray();
    var collector = new AnalysisEvidenceCollector(new EvidenceBudget(MaxNodes: 1, MaxEdges: 1));
    foreach (var node in nodes)
    {
      collector.RecordSeed(new MarkRecord("test", node, null, null, "test"));
    }

    var result = collector.Complete(Array.Empty<RuleDecision>());

    Assert.True(result.Graph.Budget.WasTruncated);
  }

  [Fact]
  public void Complete_WhenBudgetIsExceeded_RetainsDecisionEvidenceRoot()
  {
    var tree = CSharpSyntaxTree.ParseText("class C { void First() { } void Second() { } }");
    var methods = tree.GetRoot().DescendantNodes()
      .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
      .ToArray();
    var collector = new AnalysisEvidenceCollector(new EvidenceBudget(MaxNodes: 1, MaxEdges: 1));
    foreach (var method in methods)
    {
      collector.RecordSeed(new MarkRecord("seed", method, null, null, "seed"));
    }

    var result = collector.Complete(methods
      .Select(method => new RuleDecision(method, method, DecisionActionKind.Delete, "delete"))
      .ToArray());

    Assert.All(result.Decisions, decision => Assert.Contains(result.Graph.Nodes, node =>
      node.Id == decision.EvidenceRootId && node.Kind == AnalysisEvidenceKind.Decision));
  }

  [Fact]
  public void Collector_WhenPropagationAndLiftUseMultipleInputs_RecordsAllDerivedEdges()
  {
    var tree = CSharpSyntaxTree.ParseText("class C { void M() { int first = 1; int second = 2; } }");
    var nodes = tree.GetRoot().DescendantNodes().Take(3).ToArray();
    var first = new MarkRecord("seed-one", nodes[0], null, null, "first");
    var second = new MarkRecord("seed-two", nodes[1], null, null, "second");
    var propagated = new PropagatedMarkRecord("propagate", new MarkRecord("propagate", nodes[2], null, null, "derived"), first, 1);
    var lifted = new NLISSN.Core.Lifting.LiftedMarkRecord(
      "lift",
      new MarkRecord("lift", nodes[0], null, null, "lifted"),
      first,
      1);
    var collector = new AnalysisEvidenceCollector();

    collector.RecordSeed(first);
    collector.RecordSeed(second);
    collector.RecordPropagation("propagate", new[] { first, second }, new[] { propagated });
    collector.RecordLift("lift", new[] { first }, new[] { propagated }, new[] { lifted });

    var graph = collector.Complete(Array.Empty<RuleDecision>()).Graph;

    Assert.Equal(4, graph.Edges.Count(edge => edge.Kind == AnalysisEvidenceEdgeKind.DerivedFrom));
  }

  [Fact]
  public void AnalyzeFromArgs_WhenEvidenceJsonIsRequested_WritesStableProjection()
  {
    var sourcePath = Path.Combine(Path.GetTempPath(), $"decision-evidence-{Guid.NewGuid():N}.cs");
    var outputPath = sourcePath + ".json";
    File.WriteAllText(sourcePath, ReachabilitySources.ReachabilityIgnoresConfiguredMethodNamesSource);
    try
    {
      var host = new CommandHost(RuleRegistry.CreateDefaultRules());

      var result = host.AnalyzeFromArgs(new[]
      {
        sourcePath,
        "--delete-unreachable-methods",
        "--evidence-json",
        outputPath
      });

      Assert.True(File.Exists(outputPath));
      var projection = JsonSerializer.Deserialize<AnalysisEvidenceGraph>(
        File.ReadAllText(outputPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      Assert.NotNull(projection);
      Assert.Equal(result.Evidence!.Nodes.Select(node => node.Id), projection!.Nodes.Select(node => node.Id));
    }
    finally
    {
      File.Delete(sourcePath);
      File.Delete(outputPath);
    }
  }

  [Fact]
  public void Combine_WhenDirectoryBudgetIsExceeded_EmitsTruncationNode()
  {
    var graphs = Enumerable.Range(0, 2)
      .Select(index => new AnalysisEvidenceGraph(
        new[]
        {
          new AnalysisEvidenceNode($"node-{index}", AnalysisEvidenceKind.SeedMark, null, null, null,
            AnalysisEvidenceState.Available, string.Empty)
        },
        Array.Empty<AnalysisEvidenceEdge>(),
        new EvidenceBudgetSummary(1, 0, 10, 10, false)))
      .ToArray();

    var combined = AnalysisEvidenceGraph.Combine(graphs, new EvidenceBudget(MaxNodes: 1, MaxEdges: 1));

    Assert.True(combined.Budget.WasTruncated);
    Assert.Contains(combined.Nodes, node => node.Kind == AnalysisEvidenceKind.Truncated);
  }

  [Fact]
  public void Combine_WhenDirectoryByteBudgetIsExceeded_ReportsBoundedProjection()
  {
    var graphs = Enumerable.Range(0, 4)
      .Select(index => new AnalysisEvidenceGraph(
        new[]
        {
          new AnalysisEvidenceNode(
            $"node-{index}",
            AnalysisEvidenceKind.SeedMark,
            null,
            null,
            null,
            AnalysisEvidenceState.Available,
            new string('x', 240))
        },
        Array.Empty<AnalysisEvidenceEdge>(),
        new EvidenceBudgetSummary(1, 0, 10, 10, false)))
      .ToArray();

    var combined = AnalysisEvidenceGraph.Combine(
      graphs,
      new EvidenceBudget(MaxSerializedBytes: 400));

    Assert.True(combined.Budget.WasTruncated);
    Assert.InRange(combined.Budget.SerializedByteCount, 0, combined.Budget.MaxSerializedBytes);
    Assert.Contains(combined.Nodes, node => node.Kind == AnalysisEvidenceKind.Truncated);
  }

  [Fact]
  public void RecordRelationQuery_WhenQueryIsTruncated_ProjectsStatusAndCacheHit()
  {
    var collector = new AnalysisEvidenceCollector();
    var query = new CpgRelationQuery(
      CpgRelationProfile.StructuralContainment,
      CpgQueryDirection.Bidirectional,
      new CpgNodeSelector(),
      null,
      new NLCPGTraversalBudget(1, 1, 1, 1, 1),
      NLCPGCapability.SyntaxSemantic);
    var result = new CpgRelationQueryResult(
      Array.Empty<NLCPG.Model.NLCPGNode>(),
      Array.Empty<NLCPG.Model.NLCPGEdge>(),
      Array.Empty<CpgRelationPath>(),
      CpgQueryStatus.Truncated,
      "maxVisitedNodes",
      Array.Empty<CpgShardUnavailableResult>(),
      NLCPGCapability.SyntaxSemantic,
      true,
      1,
      0,
      0);

    collector.RecordRelationQuery(query, result);

    var evidence = collector.Complete(Array.Empty<RuleDecision>()).Graph;
    var node = Assert.Single(evidence.Nodes, node => node.Kind == AnalysisEvidenceKind.Query);
    Assert.Equal(AnalysisEvidenceState.Truncated, node.State);
    Assert.Contains("StructuralContainment", node.SummaryKey);
    Assert.Contains("True", node.SummaryKey);
    Assert.Contains(evidence.Edges, edge => edge.Kind == AnalysisEvidenceEdgeKind.TruncatedBy);
  }

  [Fact]
  public void RecordFlowSummary_WhenResolvedAndBlocked_ProjectsUsesSummaryStates()
  {
    var tree = CSharpSyntaxTree.ParseText("class C { void M() { Call(); } void Call() { } }");
    var invocation = tree.GetRoot().DescendantNodes()
      .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
      .Single();
    var key = new FlowSummaryMethodKey("Demo", "Demo.C", "Call", 0, 0,
      Array.Empty<string>(), Array.Empty<string>());
    var mapping = new FlowSummaryMapping(
      FlowSummaryEndpoint.Receiver,
      FlowSummaryEndpoint.Return,
      FlowSummaryMappingKind.Explicit);
    var collector = new AnalysisEvidenceCollector();

    collector.RecordFlowSummary(invocation, new ResolvedCallFlow(
      ResolvedCallFlowStatus.Resolved, FlowSummaryResolution.Project, key, mapping, null));
    collector.RecordFlowSummary(invocation, new ResolvedCallFlow(
      ResolvedCallFlowStatus.Blocked, FlowSummaryResolution.User, key,
      mapping with { Kind = FlowSummaryMappingKind.Block }, "Blocked by configured summary."));

    var nodes = collector.Complete(Array.Empty<RuleDecision>()).Graph.Nodes
      .Where(node => node.Kind == AnalysisEvidenceKind.UsesSummary)
      .OrderBy(node => node.SummaryKey, StringComparer.Ordinal)
      .ToArray();

    Assert.Equal(2, nodes.Length);
    Assert.Contains(nodes, node => node.State == AnalysisEvidenceState.Available &&
      node.SummaryKey!.Contains(key.StableKey, StringComparison.Ordinal));
    Assert.Contains(nodes, node => node.State == AnalysisEvidenceState.Rejected);
  }

  [Fact]
  public void Complete_WhenReplaceWinsAndParentCoversChild_ProjectsRejectionAndMerge()
  {
    var tree = CSharpSyntaxTree.ParseText("class C { void M() { if (true) { int value = 1; } } }");
    var method = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single();
    var local = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax>().Single();
    var delete = CreateUnit("delete", method, DecisionActionKind.Delete, "delete");
    var replace = CreateUnit("replace", method, DecisionActionKind.Replace, "replace");
    var child = CreateUnit("child", local, DecisionActionKind.Delete, "child");
    var collector = new AnalysisEvidenceCollector();

    collector.RecordProposal("delete", Array.Empty<object>(), new[] { delete });
    collector.RecordProposal("replace", Array.Empty<object>(), new[] { replace });
    collector.RecordProposal("child", Array.Empty<object>(), new[] { child });
    var result = collector.Complete(new[]
    {
      new RuleDecision(method, method, DecisionActionKind.Replace, "replace")
    });

    Assert.Contains(result.Graph.Edges, edge => edge.Kind == AnalysisEvidenceEdgeKind.RejectedBy);
    Assert.Contains(result.Graph.Edges, edge => edge.Kind == AnalysisEvidenceEdgeKind.MergedInto);
  }

  private static Dictionary<string, string> CreateOptions(int maxDegreeOfParallelism)
  {
    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["max-degree-of-parallelism"] = maxDegreeOfParallelism.ToString(),
      ["enable-group-parallelism"] = "true"
    };
  }

  private static bool HasSeedPath(string rootId, AnalysisEvidenceGraph evidence)
  {
    var nodesById = evidence.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
    var edgesByTarget = evidence.Edges
      .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).ToArray(), StringComparer.Ordinal);
    var pending = new Queue<string>();
    var visited = new HashSet<string>(StringComparer.Ordinal);
    pending.Enqueue(rootId);
    while (pending.Count > 0)
    {
      var current = pending.Dequeue();
      if (!visited.Add(current) || !nodesById.TryGetValue(current, out var node))
      {
        continue;
      }

      if (node.Kind == AnalysisEvidenceKind.SeedMark)
      {
        return true;
      }

      if (edgesByTarget.TryGetValue(current, out var parents))
      {
        foreach (var parent in parents)
        {
          pending.Enqueue(parent);
        }
      }
    }

    return false;
  }

  private static DecisionUnit CreateUnit(
    string ruleId,
    Microsoft.CodeAnalysis.SyntaxNode node,
    DecisionActionKind action,
    string reason)
  {
    var fragment = DecisionCpgFactory.CreateFragment($"fragment:{ruleId}", node, "anchor", action);
    var unit = DecisionCpgFactory.CreateUnit(ruleId, action, fragment, reason);
    return new DecisionUnit(
      ruleId,
      action,
      unit,
      new[] { fragment },
      Array.Empty<NLCPG.Model.NLCPGEdge>(),
      new Dictionary<NLCPG.Model.NodeId, Microsoft.CodeAnalysis.SyntaxNode>
      {
        [fragment.NodeId!.Value] = node
      },
      reason: reason);
  }
}
