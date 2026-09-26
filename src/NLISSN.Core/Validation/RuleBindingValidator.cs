using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using NLISSN.Core.Analysis.ExpressionPropagation;
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
  internal AnalysisValidationReport Validate(
    AnalysisSession session,
    CompiledRuleGraph graph,
    RuleGraphExecutionResult execution)
  {
    ArgumentNullException.ThrowIfNull(session);
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
        ValidatePrimaryBinding(session, mark, issues);
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
        !IsRegisteredPropagationPayload(propagated))
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

  private static bool IsRegisteredPropagationPayload(PropagatedMarkRecord propagated)
  {
    return propagated.Payload switch
    {
      ExpressionTopologyPayload => propagated.Mark.FactKind is
        RuleFactKind.TargetExpression or
        RuleFactKind.FlowLogicalExpression or
        RuleFactKind.FlowUnaryExpression or
        RuleFactKind.FlowConditionalExpression,
      ExternalSummaryFlowPayload => string.Equals(
        propagated.Mark.SemanticTag?.Value,
        ExternalSummaryFlowPayload.SemanticTag.Value,
        StringComparison.Ordinal),
      MethodParameterUsagePayload or LocalFunctionParameterUsagePayload or
        IndexerParameterUsagePayload or DelegateUsagePayload or
        ExtensionMethodMappedCallsitePayload or DeclarationHostPayload => true,
      _ => false,
    };
  }

  private static bool HasMatchingRelationPort(PropagatedMarkRecord propagated)
  {
    RuleFactKind? expectedPort = propagated.Payload switch
    {
      MethodParameterUsagePayload or LocalFunctionParameterUsagePayload or IndexerParameterUsagePayload =>
        RuleFactKind.RelationParameterUsage,
      DelegateUsagePayload => RuleFactKind.RelationDelegateUsage,
      ExtensionMethodMappedCallsitePayload => RuleFactKind.RelationExtensionUsage,
      DeclarationHostPayload => RuleFactKind.RelationDeclarationHost,
      _ => null,
    };

    return expectedPort is null ||
      RuleFactKindDescriptor.Resolve(propagated.Mark.FactKind, propagated.Mark.SemanticTag) == expectedPort;
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
    if (mark.FactKind is null && mark.SemanticTag is null)
    {
      issues.Add(CreateIssue(
        "BIND002",
        $"{node.NodeId.Value}:{mark.SyntaxNode.SpanStart}",
        "An emitted mark does not carry a semantic tag.",
        mark.RuleId));
      return;
    }

    var syntaxKind = (SyntaxKind)mark.SyntaxNode.RawKind;
    if (!string.Equals(mark.RuleId, node.NodeId.RuleId, StringComparison.Ordinal))
    {
      issues.Add(CreateIssue(
        "BIND008",
        $"{node.NodeId.Value}:{mark.RuleId}:{mark.SyntaxNode.SpanStart}",
        "An emitted mark is attributed to a different rule than its producer node.",
        mark.RuleId));
    }

    if (!node.ProducedSyntax.Any(output =>
      RuleFactKindDescriptor.Matches(
        output.FactKind,
        output.SemanticTag,
        mark.FactKind,
        mark.SemanticTag) &&
      output.SyntaxKinds.Contains(syntaxKind)))
    {
      issues.Add(CreateIssue(
        "BIND003",
        $"{node.NodeId.Value}:{mark.SyntaxNode.SpanStart}:" +
        $"{mark.SemanticTag?.Value ?? RuleFactKindDescriptor.GetDisplayName(mark.FactKind!.Value)}",
        "An emitted mark does not match the producer's declared syntax output.",
        mark.RuleId));
    }
  }

  private static void ValidatePrimaryBinding(
    AnalysisSession session,
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

    var graphNode = mark.PrimaryGraphNode.Value;
    if (graphNode.IsImplicit)
    {
      return;
    }

    var bound = session.FindGraphNodeById(graphNode.NodeId ?? default);
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

    if (!string.Equals(session.Graph.ResolveFilePath(graphNode), mark.SyntaxNode.SyntaxTree.FilePath, StringComparison.Ordinal) ||
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
