using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 封装删除类标记需要的符号与语法事实，集中保守地处理不可解析的绑定。
public static class DeclarationMarkRuleHelpers
{
    // 解析 delete-class 选项里的目标类名列表，并去掉空值与重复项。
    public static IReadOnlyList<string> ParseTargetTypeNames(IMarkRuleContext context)
    {
        return context.DeleteClassNames;
    }

    // 只为名称直接命中的类声明生成删除类 seed mark。
    public static IEnumerable<MarkRecord> BuildDeclarationMarks(IMarkRuleContext context, SyntaxNode root, string ruleId)
    {
        var targetTypeNames = ParseTargetTypeNames(context);
        if (targetTypeNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        return root.DescendantNodes()
          .OfType<ClassDeclarationSyntax>()
          .Where(type => targetTypeNames.Contains(type.Identifier.ValueText, StringComparer.Ordinal))
          .Select(type => MarkRecordFactory.Create(
            ruleId,
            type,
            $"Class declaration '{type.Identifier.ValueText}' matches delete-class target."))
          .ToList();
    }

    // 找出语义上引用目标类的最小表达式命中，并按位置稳定去重排序。
    public static IEnumerable<MarkRecord> BuildExpressionMarks(IMarkRuleContext context, SyntaxNode root, string ruleId, IReadOnlyCollection<SyntaxKind> allowedKinds)
    {
        var targetTypeNames = ParseTargetTypeNames(context);
        if (targetTypeNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        return root.DescendantNodes()
          .OfType<ExpressionSyntax>()
          .Where(expression => expression is not TypeSyntax)
          .Where(expression => allowedKinds.Contains(expression.Kind()))
          .Where(expression => ReferencesTargetType(context, expression, targetTypeNames))
          .Where(expression => !HasTargetAncestorExpression(context, expression, targetTypeNames))
          .Select(expression => MarkRecordFactory.Create(
            ruleId,
            expression,
            $"Expression references delete-class target '{string.Join(",", targetTypeNames)}'."))
          .DistinctBy(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind))
          .OrderBy(mark => mark.SyntaxNode.SpanStart)
          .ThenByDescending(mark => mark.SyntaxNode.Span.Length)
          .ToList();
    }

    // 只在声明位置上标记目标类 TypeSyntax，避免把泛型嵌套类型片段重复扩散到多个宿主。
    public static IEnumerable<MarkRecord> BuildTypeSyntaxMarks(IMarkRuleContext context, SyntaxNode root, string ruleId, IReadOnlyCollection<SyntaxKind> allowedKinds)
    {
        var targetTypeNames = ParseTargetTypeNames(context);
        if (targetTypeNames.Count == 0)
        {
            return Array.Empty<MarkRecord>();
        }

        return root.DescendantNodes()
          .OfType<TypeSyntax>()
          .Where(typeSyntax => allowedKinds.Contains(typeSyntax.Kind()))
          .Where(IsTypeSyntaxPosition)
          .Where(typeSyntax => ReferencesTargetType(context, typeSyntax, targetTypeNames))
          .Where(typeSyntax => !HasTargetAncestorType(context, typeSyntax, targetTypeNames))
          .Where(typeSyntax => !HasTargetDescendantType(context, typeSyntax, targetTypeNames))
          .Select(typeSyntax => MarkRecordFactory.Create(
            ruleId,
            typeSyntax,
            $"Type syntax references delete-class target '{string.Join(",", targetTypeNames)}'."))
          .DistinctBy(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind))
          .OrderBy(mark => mark.SyntaxNode.SpanStart)
          .ThenByDescending(mark => mark.SyntaxNode.Span.Length)
          .ToList();
    }

    private static bool HasTargetAncestorExpression(IMarkRuleContext context, ExpressionSyntax expression, IReadOnlyList<string> targetTypeNames)
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

            if (ReferencesTargetType(context, ancestor, targetTypeNames))
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

    private static bool HasTargetDescendantType(IMarkRuleContext context, TypeSyntax typeSyntax, IReadOnlyList<string> targetTypeNames)
    {
        foreach (var descendant in typeSyntax.DescendantNodes().OfType<TypeSyntax>())
        {
            if (ReferencesTargetType(context, descendant, targetTypeNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTargetAncestorType(IMarkRuleContext context, TypeSyntax typeSyntax, IReadOnlyList<string> targetTypeNames)
    {
        foreach (var ancestor in typeSyntax.Ancestors().OfType<TypeSyntax>())
        {
            if (ReferencesTargetType(context, ancestor, targetTypeNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesTargetType(IMarkRuleContext context, ExpressionSyntax expression, IReadOnlyList<string> targetTypeNames)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression);
        if (MatchesTargetType(typeInfo.Type, targetTypeNames) ||
            MatchesTargetType(typeInfo.ConvertedType, targetTypeNames))
        {
            return true;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression).Symbol;
        if (MatchesTargetSymbol(symbol, targetTypeNames))
        {
            return true;
        }

        var operation = context.SemanticModel.GetOperation(expression);
        if (operation is null)
        {
            return false;
        }

        return ReferencesTargetType(operation, targetTypeNames);
    }

    private static bool ReferencesTargetType(IMarkRuleContext context, TypeSyntax typeSyntax, IReadOnlyList<string> targetTypeNames)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(typeSyntax);
        if (MatchesTargetType(typeInfo.Type, targetTypeNames) ||
            MatchesTargetType(typeInfo.ConvertedType, targetTypeNames))
        {
            return true;
        }

        return MatchesTargetSymbol(
          context.SemanticModel.GetSymbolInfo(typeSyntax).Symbol,
          targetTypeNames);
    }

    private static bool ReferencesTargetType(IOperation operation, IReadOnlyList<string> targetTypeNames)
    {
        if (MatchesTargetSymbol(ResolveOperationSymbol(operation), targetTypeNames))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ReferencesTargetType(child, targetTypeNames))
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

    private static bool MatchesTargetSymbol(ISymbol? symbol, IReadOnlyList<string> targetTypeNames)
    {
        if (symbol is null)
        {
            return false;
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            return MatchesTargetType(namedType, targetTypeNames);
        }

        return symbol switch
        {
            IMethodSymbol method => MatchesTargetType(method.ContainingType, targetTypeNames),
            IPropertySymbol property => MatchesTargetType(property.ContainingType, targetTypeNames),
            IFieldSymbol field => MatchesTargetType(field.ContainingType, targetTypeNames),
            _ => false
        };
    }

    private static bool MatchesTargetType(ITypeSymbol? typeSymbol, IReadOnlyList<string> targetTypeNames)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return targetTypeNames.Contains(namedType.Name, StringComparer.Ordinal);
    }
}
