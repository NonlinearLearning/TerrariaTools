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
        if (targetNames.DisplayNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        var marks = new List<MarkRecord>();
        foreach (var expression in context.EnumerateAllowedExpressions(root, allowedKinds))
        {
            if (!IsRootedAtTarget(context, expression, targetNames))
            {
                continue;
            }

            var markRegion = context.AnalyzeMarkRegion(expression);

            if (!HasCpgNodeInRegion(context, expression, markRegion))
            {
                continue;
            }

            if (HasRootedObjectAncestorInRegion(context, expression, targetNames, markRegion))
            {
                continue;
            }

            context.TryResolvePrimaryGraphNode(expression, out var primaryGraphNode);
            marks.Add(MarkRecordFactory.Create(
              ruleId,
              expression,
              $"Expression is rooted at target '{string.Join(",", targetNames.DisplayNames)}' within mark region {markRegion.Span}.") with
            {
                PrimaryGraphNode = primaryGraphNode
            });
        }

        return FinalizeMarks(marks);
    }

    public static IEnumerable<MarkRecord> BuildDefinitionLeftValueMarks(RuleContext context, SyntaxNode root, string ruleId)
    {
        var targetNames = ParseTargetNames(context);
        if (targetNames.DisplayNames.Count == 0)
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

            if (!targetNames.Lookup.Contains(declarator.Identifier.ValueText))
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

    private static TargetNameDescriptor ParseTargetNames(RuleContext context)
    {
        return context.GetTargetNameDescriptor();
    }

    private static IEnumerable<MarkRecord> FinalizeMarks(IEnumerable<MarkRecord> marks)
    {
        return marks
          .DistinctBy(candidate => (candidate.SyntaxNode.SpanStart, candidate.SyntaxNode.Span.Length))
          .OrderBy(candidate => candidate.SyntaxNode.SpanStart)
          .ThenByDescending(candidate => candidate.SyntaxNode.Span.Length)
          .ToList();
    }

    private static bool HasRootedObjectAncestorInRegion(RuleContext context, ExpressionSyntax expression, TargetNameDescriptor targetNames, MarkCodeRegion markRegion)
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

    private static bool IsRootedAtTarget(RuleContext context, ExpressionSyntax expression, TargetNameDescriptor targetNames)
    {
        return context.GetCachedTargetMatch(expression, targetNames, () =>
        {
            if (expression is MemberBindingExpressionSyntax memberBinding &&
                ReferencesTargetMemberBinding(context, memberBinding, targetNames))
            {
                return true;
            }

            var operation = context.GetCachedOperation(expression);
            return operation is not null && ReferencesTarget(operation, targetNames);
        });
    }

    private static bool ReferencesTarget(IOperation operation, TargetNameDescriptor targetNames)
    {
        if (operation is ILocalReferenceOperation localReference &&
            targetNames.Lookup.Contains(localReference.Local.Name))
        {
            return true;
        }

        if (operation is IParameterReferenceOperation parameterReference &&
            targetNames.Lookup.Contains(parameterReference.Parameter.Name))
        {
            return true;
        }

        if (operation is IFieldReferenceOperation fieldReference &&
            targetNames.Lookup.Contains(fieldReference.Field.Name))
        {
            return true;
        }

        if (operation is IPropertyReferenceOperation propertyReference &&
            targetNames.Lookup.Contains(propertyReference.Property.Name))
        {
            return true;
        }

        if (operation is IInvocationOperation invocation &&
            targetNames.Lookup.Contains(invocation.TargetMethod.Name))
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
            targetNames.Lookup.Contains(objectCreation.Constructor.ContainingType.Name))
        {
            return true;
        }

        if (operation is ILiteralOperation literalOperation &&
            literalOperation.ConstantValue.HasValue &&
            literalOperation.ConstantValue.Value is not null &&
            targetNames.Lookup.Contains(literalOperation.ConstantValue.Value.ToString()!))
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

    private static bool ReferencesTargetMemberBinding(RuleContext context, MemberBindingExpressionSyntax memberBinding, TargetNameDescriptor targetNames)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(memberBinding).Symbol;
        if (symbol is null)
        {
            return false;
        }

        return targetNames.Lookup.Contains(symbol.Name);
    }

    private static bool IsTargetInstanceReference(IInstanceReferenceOperation instanceReference, TargetNameDescriptor targetNames)
    {
        return instanceReference.Syntax switch
        {
            ThisExpressionSyntax => targetNames.Lookup.Contains("this"),
            BaseExpressionSyntax => targetNames.Lookup.Contains("base"),
            _ => false
        };
    }

    private static bool HasCpgNodeInRegion(RuleContext context, ExpressionSyntax expression, MarkCodeRegion markRegion)
    {
        return context.ContainsPrimaryGraphNodeInRegion(expression, markRegion.Span);
    }
}
