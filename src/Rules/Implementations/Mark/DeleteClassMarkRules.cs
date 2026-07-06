using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Marking;

namespace Rules;

internal static class DeleteClassMarkRuleHelpers
{
    internal static IReadOnlyList<string> ParseTargetClassNames(RuleContext context)
    {
        if (!context.TryGetOption("delete-class", out var className) ||
            string.IsNullOrWhiteSpace(className))
        {
            return Array.Empty<string>();
        }

        return className
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.Ordinal)
          .ToList();
    }

    internal static IEnumerable<MarkRecord> BuildDeclarationMarks(
      RuleContext context,
      SyntaxNode root,
      string ruleId)
    {
        var targetClassNames = ParseTargetClassNames(context);
        if (targetClassNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        return root.DescendantNodes()
          .OfType<ClassDeclarationSyntax>()
          .Where(type => targetClassNames.Contains(type.Identifier.ValueText, StringComparer.Ordinal))
          .Select(type => RuleAnalysisHelpers.CreateMark(
            ruleId,
            type,
            $"Class declaration '{type.Identifier.ValueText}' matches delete-class target."))
          .ToList();
    }

    internal static IEnumerable<MarkRecord> BuildExpressionMarks(
      RuleContext context,
      SyntaxNode root,
      string ruleId,
      IReadOnlyCollection<SyntaxKind> allowedKinds)
    {
        var targetClassNames = ParseTargetClassNames(context);
        if (targetClassNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        return root.DescendantNodes()
          .OfType<ExpressionSyntax>()
          .Where(expression => expression is not TypeSyntax)
          .Where(expression => allowedKinds.Contains(expression.Kind()))
          .Where(expression => ReferencesTargetType(context, expression, targetClassNames))
          .Where(expression => !HasTargetAncestorExpression(context, expression, targetClassNames))
          .Select(expression => RuleAnalysisHelpers.CreateMark(
            ruleId,
            expression,
            $"Expression references delete-class target '{string.Join(",", targetClassNames)}'."))
          .DistinctBy(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind))
          .OrderBy(mark => mark.SyntaxNode.SpanStart)
          .ThenByDescending(mark => mark.SyntaxNode.Span.Length)
          .ToList();
    }

    internal static IEnumerable<MarkRecord> BuildTypeSyntaxMarks(
      RuleContext context,
      SyntaxNode root,
      string ruleId,
      IReadOnlyCollection<SyntaxKind> allowedKinds)
    {
        var targetClassNames = ParseTargetClassNames(context);
        if (targetClassNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        return root.DescendantNodes()
          .OfType<TypeSyntax>()
          .Where(typeSyntax => allowedKinds.Contains(typeSyntax.Kind()))
          .Where(IsTypeSyntaxPosition)
          .Where(typeSyntax => ReferencesTargetType(context, typeSyntax, targetClassNames))
          .Where(typeSyntax => !HasTargetAncestorType(context, typeSyntax, targetClassNames))
          .Where(typeSyntax => !HasTargetDescendantType(context, typeSyntax, targetClassNames))
          .Select(typeSyntax => RuleAnalysisHelpers.CreateMark(
            ruleId,
            typeSyntax,
            $"Type syntax references delete-class target '{string.Join(",", targetClassNames)}'."))
          .DistinctBy(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind))
          .OrderBy(mark => mark.SyntaxNode.SpanStart)
          .ThenByDescending(mark => mark.SyntaxNode.Span.Length)
          .ToList();
    }

    private static bool HasTargetAncestorExpression(
      RuleContext context,
      ExpressionSyntax expression,
      IReadOnlyList<string> targetClassNames)
    {
        foreach (var ancestor in expression.Ancestors().OfType<ExpressionSyntax>())
        {
            if (ancestor is not MemberAccessExpressionSyntax and
                not InvocationExpressionSyntax and
                not ElementAccessExpressionSyntax and
                not ConditionalAccessExpressionSyntax and
                not ObjectCreationExpressionSyntax and
                not ImplicitObjectCreationExpressionSyntax)
            {
                continue;
            }

            if (ReferencesTargetType(context, ancestor, targetClassNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeSyntaxPosition(TypeSyntax typeSyntax)
    {
        SyntaxNode current = typeSyntax;
        var parent = current.Parent;
        while (parent is TypeSyntax)
        {
            current = parent;
            parent = parent.Parent;
        }

        if (parent is TypeArgumentListSyntax typeArgumentList &&
            typeArgumentList.Parent is TypeSyntax genericType)
        {
            return IsTypeSyntaxPosition(genericType);
        }

        return parent switch
        {
            VariableDeclarationSyntax variableDeclaration
              when ReferenceEquals(variableDeclaration.Type, current) => true,
            MethodDeclarationSyntax methodDeclaration
              when ReferenceEquals(methodDeclaration.ReturnType, current) => true,
            DelegateDeclarationSyntax delegateDeclaration
              when ReferenceEquals(delegateDeclaration.ReturnType, current) => true,
            ParameterSyntax parameter
              when ReferenceEquals(parameter.Type, current) => true,
            PropertyDeclarationSyntax propertyDeclaration
              when ReferenceEquals(propertyDeclaration.Type, current) => true,
            IndexerDeclarationSyntax indexerDeclaration
              when ReferenceEquals(indexerDeclaration.Type, current) => true,
            SimpleBaseTypeSyntax simpleBaseType
              when ReferenceEquals(simpleBaseType.Type, current) => true,
            _ => false
        };
    }

    private static bool HasTargetDescendantType(
      RuleContext context,
      TypeSyntax typeSyntax,
      IReadOnlyList<string> targetClassNames)
    {
        foreach (var descendant in typeSyntax.DescendantNodes().OfType<TypeSyntax>())
        {
            if (ReferencesTargetType(context, descendant, targetClassNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTargetAncestorType(
      RuleContext context,
      TypeSyntax typeSyntax,
      IReadOnlyList<string> targetClassNames)
    {
        foreach (var ancestor in typeSyntax.Ancestors().OfType<TypeSyntax>())
        {
            if (ReferencesTargetType(context, ancestor, targetClassNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesTargetType(
      RuleContext context,
      ExpressionSyntax expression,
      IReadOnlyList<string> targetClassNames)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression);
        if (MatchesTargetType(typeInfo.Type, targetClassNames) ||
            MatchesTargetType(typeInfo.ConvertedType, targetClassNames))
        {
            return true;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression).Symbol;
        if (MatchesTargetSymbol(symbol, targetClassNames))
        {
            return true;
        }

        var operation = context.SemanticModel.GetOperation(expression);
        if (operation is null)
        {
            return false;
        }

        return ReferencesTargetType(operation, targetClassNames);
    }

    private static bool ReferencesTargetType(
      RuleContext context,
      TypeSyntax typeSyntax,
      IReadOnlyList<string> targetClassNames)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(typeSyntax);
        if (MatchesTargetType(typeInfo.Type, targetClassNames) ||
            MatchesTargetType(typeInfo.ConvertedType, targetClassNames))
        {
            return true;
        }

        return MatchesTargetSymbol(
          context.SemanticModel.GetSymbolInfo(typeSyntax).Symbol,
          targetClassNames);
    }

    private static bool ReferencesTargetType(
      IOperation operation,
      IReadOnlyList<string> targetClassNames)
    {
        if (MatchesTargetSymbol(ResolveOperationSymbol(operation), targetClassNames))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ReferencesTargetType(child, targetClassNames))
            {
                return true;
            }
        }

        return false;
    }

    private static ISymbol? ResolveOperationSymbol(IOperation operation)
    {
        return operation switch
        {
            IObjectCreationOperation objectCreation => objectCreation.Constructor?.ContainingType,
            IInvocationOperation invocation => invocation.TargetMethod,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            IMethodReferenceOperation methodReference => methodReference.Method,
            ILocalReferenceOperation localReference => localReference.Local.Type,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter.Type,
            IConversionOperation conversion when conversion.OperatorMethod is not null => conversion.OperatorMethod,
            _ => null
        };
    }

    private static bool MatchesTargetSymbol(
      ISymbol? symbol,
      IReadOnlyList<string> targetClassNames)
    {
        if (symbol is null)
        {
            return false;
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            return MatchesTargetType(namedType, targetClassNames);
        }

        return symbol switch
        {
            IMethodSymbol method => MatchesTargetType(method.ContainingType, targetClassNames),
            IPropertySymbol property => MatchesTargetType(property.ContainingType, targetClassNames),
            IFieldSymbol field => MatchesTargetType(field.ContainingType, targetClassNames),
            _ => false
        };
    }

    private static bool MatchesTargetType(
      ITypeSymbol? typeSymbol,
      IReadOnlyList<string> targetClassNames)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return targetClassNames.Contains(namedType.Name, StringComparer.Ordinal);
    }
}

public sealed class DeleteClassDeclarationMarkRule : RuleDefinitionMark
{
    public override string RuleId { get; } = DeleteClassRuleIds.DeclarationMarkRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Match class declarations by delete-class option";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.ClassDeclaration };

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildDeclarationMarks(context, root, RuleId);
    }
}

public sealed class DeleteClassExpressionMarkRule : RuleDefinitionMark
{
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression
      };

    public override string RuleId { get; } = DeleteClassRuleIds.ExpressionMarkRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Match expressions that reference the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          SupportedKinds);
    }
}

public sealed class DeleteClassTypeSyntaxMarkRule : RuleDefinitionMark
{
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.QualifiedName,
        SyntaxKind.AliasQualifiedName,
        SyntaxKind.GenericName
      };

    public override string RuleId { get; } = DeleteClassRuleIds.TypeSyntaxMarkRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Match type syntax that references the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildTypeSyntaxMarks(
          context,
          root,
          RuleId,
          SupportedKinds);
    }
}
