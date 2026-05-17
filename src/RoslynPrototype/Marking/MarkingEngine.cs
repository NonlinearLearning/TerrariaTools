using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Marking;

public sealed class MarkingEngine : IMarkingEngine
{
  public IReadOnlyList<MarkRecord> Run(
    RuleContext context,
    SyntaxNode root,
    IReadOnlyList<IDeletionRule> rules)
  {
    var seedMarks = new List<MarkRecord>();

    foreach (var rule in rules.Where(rule => rule.Metadata.EnabledByDefault)) {
      foreach (var mark in rule.Mark(context, root)) {
        ValidateHitNode(rule, mark.SyntaxNode);
        seedMarks.Add(BindMarkRecord(context, mark));
      }
    }

    return seedMarks
      .DistinctBy(mark => (mark.RuleId, mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length))
      .ToList();
  }

  internal static void ValidateHitNode(IDeletionRule rule, SyntaxNode syntaxNode)
  {
    var nodeKind = (SyntaxKind)syntaxNode.RawKind;
    if (rule.AllowedNodeKinds.Contains(nodeKind)) {
      return;
    }

    var allowedKinds = string.Join(", ", rule.AllowedNodeKinds);
    throw new InvalidOperationException(
      $"Rule '{rule.Metadata.RuleId}' emitted unsupported node kind '{nodeKind}'. Allowed node kinds: {allowedKinds}.");
  }

  internal static MarkRecord BindMarkRecord(RuleContext context, MarkRecord candidate)
  {
    var annotation = candidate.Annotation ?? new SyntaxAnnotation("RuleHitNode", Guid.NewGuid().ToString("N"));
    var primaryGraphNode = candidate.PrimaryGraphNode ?? ResolvePrimaryGraphNode(context.Graph, candidate.SyntaxNode);
    if (primaryGraphNode is null) {
      throw new InvalidOperationException(
        $"Could not bind syntax node '{candidate.SyntaxNode.Kind()}' to a graph node.");
    }

    return candidate with { Annotation = annotation, PrimaryGraphNode = primaryGraphNode };
  }

  internal static PropagatedMarkRecord BindPropagatedMarkRecord(RuleContext context, PropagatedMarkRecord candidate)
  {
    return candidate with
    {
      Mark = BindMarkRecord(context, candidate.Mark),
      SourceMark = BindMarkRecord(context, candidate.SourceMark)
    };
  }

  private static RoslynCpgNode? ResolvePrimaryGraphNode(RoslynCpgGraph graph, SyntaxNode syntaxNode)
  {
    var filePath = syntaxNode.SyntaxTree.FilePath;
    if (string.IsNullOrWhiteSpace(filePath)) {
      return null;
    }

    return graph.Nodes
      .Where(node =>
        !node.IsImplicit &&
        string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
        node.SpanStart == syntaxNode.SpanStart &&
        node.SpanEnd == syntaxNode.Span.End)
      .OrderBy(GetBindingPriority)
      .FirstOrDefault();
  }

  private static int GetBindingPriority(RoslynCpgNode node)
  {
    return node.Kind switch
    {
      RoslynCpgNodeKind.Method => 0,
      RoslynCpgNodeKind.MethodParameter => 1,
      RoslynCpgNodeKind.CallSite => 2,
      RoslynCpgNodeKind.MemberAccess => 3,
      RoslynCpgNodeKind.Reference => 4,
      RoslynCpgNodeKind.Operation => 5,
      RoslynCpgNodeKind.OpInvocation => 6,
      RoslynCpgNodeKind.OpBinary => 7,
      RoslynCpgNodeKind.OpAssignment => 8,
      RoslynCpgNodeKind.OpLocalReference => 9,
      RoslynCpgNodeKind.OpParameterReference => 10,
      RoslynCpgNodeKind.OpFieldReference => 11,
      RoslynCpgNodeKind.OpPropertyReference => 12,
      RoslynCpgNodeKind.SyntaxNode => 13,
      _ => 14
    };
  }
}
