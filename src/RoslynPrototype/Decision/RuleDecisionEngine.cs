using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace RoslynPrototype.Decision;

public sealed class RuleDecisionEngine
{
  private readonly IReadOnlyList<ISyntaxNodeDecisionBehavior> _behaviors;
  private readonly IDecisionBehaviorArbiter _arbiter;

  public RuleDecisionEngine()
  {
    _behaviors = new ISyntaxNodeDecisionBehavior[]
    {
      new LogicalBinaryReductionBehavior(),
      new DefaultDeleteDecisionBehavior()
    };
    _arbiter = new DecisionBehaviorArbiter();
  }

  public IReadOnlyList<RuleDecision> Decide(
    IEnumerable<MarkRecord> seedMarks,
    IEnumerable<PropagatedMarkRecord> propagatedMarks)
  {
    var candidates = seedMarks
      .Select(mark => new DecisionCandidate(mark, null, false))
      .Concat(propagatedMarks.Select(mark => new DecisionCandidate(mark.Mark, mark.SourceMark, true)))
      .ToList();

    var reducibleLogicalHosts = BuildReducibleLogicalHosts(candidates);
    var context = new DecisionContext(reducibleLogicalHosts);
    var decisions = new List<RuleDecision>();

    foreach (var candidate in candidates) {
      var proposals = _behaviors
        .Where(behavior => behavior.CanHandle(context, candidate))
        .OrderByDescending(behavior => behavior.Priority)
        .Select(behavior => behavior.Decide(context, candidate))
        .ToList();
      decisions.Add(_arbiter.Resolve(context, candidate, proposals));
    }

    return decisions;
  }

  private static IReadOnlySet<SyntaxNode> BuildReducibleLogicalHosts(
    IEnumerable<DecisionCandidate> candidates)
  {
    var hosts = new HashSet<SyntaxNode>();
    foreach (var candidate in candidates) {
      if (!candidate.IsPropagated || candidate.SourceMark is null) {
        continue;
      }

      if (candidate.Mark.SyntaxNode is not BinaryExpressionSyntax binaryExpression) {
        continue;
      }

      if (!binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
          !binaryExpression.IsKind(SyntaxKind.LogicalOrExpression)) {
        continue;
      }

      var sourceNode = candidate.SourceMark!.SyntaxNode;
      var leftContainsSource = binaryExpression.Left.Span.Contains(sourceNode.Span);
      var rightContainsSource = binaryExpression.Right.Span.Contains(sourceNode.Span);
      if (leftContainsSource == rightContainsSource) {
        continue;
      }

      hosts.Add(binaryExpression);
    }

    return hosts;
  }
}
