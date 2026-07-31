using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Validation;

/// <summary>
/// Validates emitted structural facts after rule-graph execution has completed.
/// </summary>
public sealed class RuleBindingValidator
{
  public AnalysisValidationReport Validate(
    RuleContext context,
    CompiledRuleGraph graph,
    RuleGraphExecutionResult execution)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(execution);

    var issues = new List<ValidationIssue>();
    var nodesById = graph.Nodes.ToDictionary(node => node.NodeId);
    foreach (var result in execution.Nodes)
    {
      var node = nodesById[result.NodeId];
      if (result.Status == RuleGraphNodeStatus.Disabled && result.Result.Values.Count > 0)
      {
        issues.Add(CreateIssue(
          "BIND001",
          result.NodeId.Value,
          "A disabled rule node produced values.",
          result.NodeId.Value));
      }

      foreach (var value in result.Result.Values)
      {
        ValidatePayload(node, value, issues);
      }

      foreach (var value in result.Result.Values)
      {
        if (GetMark(value) is not { } mark)
        {
          continue;
        }

        ValidateProducedMark(node, mark, issues);
        ValidatePrimaryBinding(context, mark, issues);
      }
    }

    return AnalysisValidationReport.Create(issues);
  }

  private static void ValidatePayload(
    RuleGraphNode node,
    object value,
    ICollection<ValidationIssue> issues)
  {
    if (!IsExpectedValueType(node.Kind, value))
    {
      issues.Add(CreateIssue(
        "BIND004",
        $"{node.NodeId.Value}:{value.GetType().FullName}",
        "A rule node emitted a value type that is not registered for its rule stage.",
        node.NodeId.Value));
      return;
    }

    if (value is PropagatedMarkRecord { Payload: not null } propagated &&
        !IsRegisteredPropagationPayload(propagated.Payload))
    {
      issues.Add(CreateIssue(
        "BIND005",
        $"{node.NodeId.Value}:{propagated.Payload.GetType().FullName}",
        "A propagated mark carries a structural or unregistered payload type.",
        propagated.RuleId));
    }

    if (value is PropagatedMarkRecord { Payload: not null } relationFact &&
        !HasMatchingRelationPort(relationFact))
    {
      issues.Add(CreateIssue(
        "BIND014",
        $"{node.NodeId.Value}:{relationFact.Payload.GetType().FullName}",
        "A relation payload is attached to the wrong semantic fact port.",
        relationFact.RuleId));
    }

    if (value is LiftedMarkRecord { Payload: { } payload } lifted &&
        payload is not ILiftPayload)
    {
      issues.Add(CreateIssue(
        "BIND013",
        $"{node.NodeId.Value}:{payload.GetType().FullName}",
        "A lifted mark carries an unregistered payload type.",
        lifted.RuleId));
    }

    if (node.Kind == RuleKind.Propose && ContainsPayload(value))
    {
      issues.Add(CreateIssue(
        "BIND006",
        $"{node.NodeId.Value}:{value.GetType().FullName}",
        "A proposal-stage value retains a propagation payload instead of a decision unit.",
        node.NodeId.Value));
    }
  }

  private static bool IsExpectedValueType(RuleKind kind, object value)
  {
    return kind switch
    {
      RuleKind.Mark => value is MarkRecord,
      RuleKind.Propagate => value is PropagatedMarkRecord,
      RuleKind.Lift => value is LiftedMarkRecord,
      RuleKind.Propose => value is DecisionUnit,
      _ => false,
    };
  }

  private static bool IsRegisteredPropagationPayload(object payload)
  {
    return payload is MethodParameterUsagePayload or LocalFunctionParameterUsagePayload or
      IndexerParameterUsagePayload or DelegateUsagePayload or
      ExtensionMethodMappedCallsitePayload or DeclarationHostPayload;
  }

  private static bool HasMatchingRelationPort(PropagatedMarkRecord propagated)
  {
    var expectedPort = propagated.Payload switch
    {
      MethodParameterUsagePayload or LocalFunctionParameterUsagePayload or IndexerParameterUsagePayload =>
        RuleFactPorts.RelationParameterUsage,
      DelegateUsagePayload => RuleFactPorts.RelationDelegateUsage,
      ExtensionMethodMappedCallsitePayload => RuleFactPorts.RelationExtensionUsage,
      DeclarationHostPayload => RuleFactPorts.RelationDeclarationHost,
      _ => null,
    };

    return expectedPort is null || propagated.Mark.SemanticTag == expectedPort;
  }

  private static bool ContainsPayload(object value)
  {
    return value is PropagatedMarkRecord { Payload: not null };
  }

  private static void ValidateProducedMark(
    RuleGraphNode node,
    MarkRecord mark,
    ICollection<ValidationIssue> issues)
  {
    if (mark.SemanticTag is null)
    {
      issues.Add(CreateIssue(
        "BIND002",
        $"{node.NodeId.Value}:{mark.SyntaxNode.SpanStart}",
        "An emitted mark does not carry a semantic tag.",
        mark.RuleId));
      return;
    }

    var syntaxKind = (SyntaxKind)mark.SyntaxNode.RawKind;
    var separator = node.NodeId.Value.IndexOf(':');
    var expectedRuleId = separator >= 0 ? node.NodeId.Value[(separator + 1)..] : node.NodeId.Value;
    if (!string.Equals(mark.RuleId, expectedRuleId, StringComparison.Ordinal))
    {
      issues.Add(CreateIssue(
        "BIND008",
        $"{node.NodeId.Value}:{mark.RuleId}:{mark.SyntaxNode.SpanStart}",
        "An emitted mark is attributed to a different rule than its producer node.",
        mark.RuleId));
    }

    if (!node.ProducedSyntax.Any(output =>
      output.SemanticTag == mark.SemanticTag && output.SyntaxKinds.Contains(syntaxKind)))
    {
      issues.Add(CreateIssue(
        "BIND003",
        $"{node.NodeId.Value}:{mark.SyntaxNode.SpanStart}:{mark.SemanticTag.Value}",
        "An emitted mark does not match the producer's declared syntax output.",
        mark.RuleId));
    }
  }

  private static void ValidatePrimaryBinding(
    RuleContext context,
    MarkRecord mark,
    ICollection<ValidationIssue> issues)
  {
    if (mark.PrimaryGraphNode is null)
    {
      issues.Add(CreateIssue(
        "BIND010",
        $"{mark.RuleId}:{mark.SyntaxNode.SyntaxTree.FilePath}:{mark.SyntaxNode.SpanStart}",
        "An emitted mark has no primary graph binding.",
        mark.RuleId));
      return;
    }

    var graphNode = mark.PrimaryGraphNode;
    if (graphNode.IsImplicit)
    {
      return;
    }

    var bound = context.FindGraphNodeById(graphNode.NodeId ?? default);
    if (graphNode.NodeId is null || bound is null)
    {
      issues.Add(CreateIssue(
        "BIND011",
        $"{mark.RuleId}:{mark.SyntaxNode.SpanStart}",
        "A primary graph binding does not belong to the current graph.",
        mark.RuleId,
        graphNode.NodeId));
      return;
    }

    if (!string.Equals(graphNode.FilePath, mark.SyntaxNode.SyntaxTree.FilePath, StringComparison.Ordinal) ||
        graphNode.SpanStart != mark.SyntaxNode.SpanStart ||
        graphNode.SpanEnd != mark.SyntaxNode.Span.End)
    {
      issues.Add(CreateIssue(
        "BIND012",
        $"{mark.RuleId}:{mark.SyntaxNode.SyntaxTree.FilePath}:{mark.SyntaxNode.SpanStart}",
        "A primary graph binding does not match the mark syntax file and span.",
        mark.RuleId,
        graphNode.NodeId));
    }
  }

  private static MarkRecord? GetMark(object value)
  {
    return value switch
    {
      MarkRecord mark => mark,
      PropagatedMarkRecord propagated => propagated.Mark,
      LiftedMarkRecord lifted => lifted.Mark,
      _ => null,
    };
  }

  private static ValidationIssue CreateIssue(
    string code,
    string key,
    string message,
    string? ruleId = null,
    NodeId? nodeId = null)
  {
    return new ValidationIssue(code, ValidationSeverity.Error, $"{code}:{key}", message, ruleId, nodeId);
  }
}
