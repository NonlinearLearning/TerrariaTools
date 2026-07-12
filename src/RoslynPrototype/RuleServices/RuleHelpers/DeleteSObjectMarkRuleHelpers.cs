using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Analysis;
using Rules;

namespace RoslynPrototype.Marking;

public static class DeleteSObjectMarkRuleHelpers
{
    public static IEnumerable<MarkRecord> BuildExpressionMarks(RuleContext context, SyntaxNode root, string ruleId, IReadOnlyCollection<SyntaxKind> allowedKinds)
    {
        var targetNames = ParseTargetNames(context);
        if (targetNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        var marks = new List<MarkRecord>();
        foreach (var expression in context.EnumerateAllowedExpressions(root, allowedKinds))
        {
            var markRegion = context.AnalyzeMarkRegion(expression);
            if (!IsRootedAtTarget(context, expression, targetNames))
            {
                continue;
            }

            if (!HasCpgNodeInRegion(context, expression, markRegion))
            {
                continue;
            }

            if (HasRootedObjectAncestorInRegion(context, expression, targetNames, markRegion))
            {
                continue;
            }

            marks.Add(MarkRecordFactory.Create(
              ruleId,
              expression,
              $"Expression is rooted at target '{string.Join(",", targetNames)}' within mark region {markRegion.Span}."));
        }

        return FinalizeMarks(marks);
    }

    public static IEnumerable<MarkRecord> BuildDefinitionLeftValueMarks(RuleContext context, SyntaxNode root, string ruleId)
    {
        var targetNames = ParseTargetNames(context);
        if (targetNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        var marks = new List<MarkRecord>();
        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer is null)
            {
                continue;
            }

            if (!targetNames.Contains(declarator.Identifier.ValueText, StringComparer.Ordinal))
            {
                continue;
            }

            marks.Add(MarkRecordFactory.Create(
              ruleId,
              declarator,
              $"Definition left value '{declarator.Identifier.ValueText}' matches target name."));
        }

        return FinalizeMarks(marks);
    }

    private static IReadOnlyList<string> ParseTargetNames(RuleContext context)
    {
        if (!context.TryGetOption("target-name", out var targetName) ||
            string.IsNullOrWhiteSpace(targetName))
        {
            return Array.Empty<string>();
        }

        return targetName
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.Ordinal)
          .ToList();
    }

    private static IEnumerable<MarkRecord> FinalizeMarks(IEnumerable<MarkRecord> marks)
    {
        return marks
          .DistinctBy(candidate => (candidate.SyntaxNode.SpanStart, candidate.SyntaxNode.Span.Length))
          .OrderBy(candidate => candidate.SyntaxNode.SpanStart)
          .ThenByDescending(candidate => candidate.SyntaxNode.Span.Length)
          .ToList();
    }

    private static bool HasRootedObjectAncestorInRegion(RuleContext context, ExpressionSyntax expression, IReadOnlyList<string> targetNames, MarkCodeRegion markRegion)
    {
        foreach (var ancestor in expression.Ancestors().OfType<ExpressionSyntax>())
        {
            if (!markRegion.Span.Contains(ancestor.Span))
            {
                break;
            }

            if (ancestor is not MemberAccessExpressionSyntax and
                not ElementAccessExpressionSyntax and
                not InvocationExpressionSyntax and
                not ConditionalAccessExpressionSyntax)
            {
                continue;
            }

            if (expression is MemberBindingExpressionSyntax &&
                ancestor is InvocationExpressionSyntax or ConditionalAccessExpressionSyntax)
            {
                continue;
            }

            if (IsRootedAtTarget(context, ancestor, targetNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRootedAtTarget(RuleContext context, ExpressionSyntax expression, IReadOnlyList<string> targetNames)
    {
        if (expression is MemberBindingExpressionSyntax memberBinding &&
            ReferencesTargetMemberBinding(context, memberBinding, targetNames))
        {
            return true;
        }

        var operation = context.SemanticModel.GetOperation(expression);
        if (operation is null)
        {
            return false;
        }

        return ReferencesTarget(operation, targetNames);
    }

    private static bool ReferencesTarget(IOperation operation, IReadOnlyList<string> targetNames)
    {
        if (operation is ILocalReferenceOperation localReference &&
            targetNames.Contains(localReference.Local.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (operation is IParameterReferenceOperation parameterReference &&
            targetNames.Contains(parameterReference.Parameter.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (operation is IFieldReferenceOperation fieldReference &&
            targetNames.Contains(fieldReference.Field.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (operation is IPropertyReferenceOperation propertyReference &&
            targetNames.Contains(propertyReference.Property.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (operation is IInvocationOperation invocation &&
            targetNames.Contains(invocation.TargetMethod.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (operation is IInstanceReferenceOperation instanceReference &&
            IsTargetInstanceReference(instanceReference, targetNames))
        {
            return true;
        }

        if (operation is IObjectCreationOperation objectCreation &&
            objectCreation.Constructor is not null &&
            targetNames.Contains(objectCreation.Constructor.ContainingType.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (operation is ILiteralOperation literalOperation &&
            literalOperation.ConstantValue.HasValue &&
            literalOperation.ConstantValue.Value is not null &&
            targetNames.Contains(literalOperation.ConstantValue.Value.ToString()!, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ReferencesTarget(child, targetNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesTargetMemberBinding(RuleContext context, MemberBindingExpressionSyntax memberBinding, IReadOnlyList<string> targetNames)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(memberBinding).Symbol;
        if (symbol is null)
        {
            return false;
        }

        return targetNames.Contains(symbol.Name, StringComparer.Ordinal);
    }

    private static bool IsTargetInstanceReference(IInstanceReferenceOperation instanceReference, IReadOnlyList<string> targetNames)
    {
        return instanceReference.Syntax switch
        {
            ThisExpressionSyntax => targetNames.Contains("this", StringComparer.Ordinal),
            BaseExpressionSyntax => targetNames.Contains("base", StringComparer.Ordinal),
            _ => false
        };
    }

    private static bool HasCpgNodeInRegion(RuleContext context, ExpressionSyntax expression, MarkCodeRegion markRegion)
    {
        return context.ContainsPrimaryGraphNodeInRegion(expression, markRegion.Span);
    }
}
