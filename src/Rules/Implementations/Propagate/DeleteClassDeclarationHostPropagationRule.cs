using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassDeclarationHostPropagationRule : RuleDefinitionPropagate
{
    public override string RuleId { get; } = DeleteClassRuleIds.DeclarationHostPropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class type syntax marks to stable declaration hosts";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.BaseList,
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration,
        SyntaxKind.FieldDeclaration,
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.SimpleBaseType
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(seedMark, out var payload, out var reason))
            {
                continue;
            }

            var hostKey = DecisionCpgFactory.BuildNodeKey(payload.HostDeclaration);
            if (!knownKeys.Add(hostKey))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                payload.HostDeclaration,
                reason),
              seedMark,
              1,
              Payload: payload);
        }
    }

    private static bool TryBuildPayload(MarkRecord seedMark, out DeclarationHostPayload payload, out string reason)
    {
        payload = null!;
        reason = string.Empty;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (TryResolveFieldDeclaration(typeSyntax, out var fieldDeclaration))
        {
            payload = new DeclarationHostPayload(fieldDeclaration, DeclarationHostKind.FieldDeclaration);
            reason = "Declaration type references the delete-class target; propagate to the owning field declaration.";
            return true;
        }

        if (TryResolvePropertyDeclaration(typeSyntax, out var propertyDeclaration))
        {
            payload = new DeclarationHostPayload(propertyDeclaration, DeclarationHostKind.PropertyDeclaration);
            reason = "Declaration type references the delete-class target; propagate to the owning property declaration.";
            return true;
        }

        if (TryResolvePrivateMethodReturn(typeSyntax, out var privateMethod))
        {
            payload = new DeclarationHostPayload(privateMethod, DeclarationHostKind.MethodReturnType);
            reason = "Method return type references the delete-class target; propagate to the owning private method.";
            return true;
        }

        if (TryResolveNonPrivateMethodReturn(typeSyntax, out var publicMethod))
        {
            payload = new DeclarationHostPayload(publicMethod, DeclarationHostKind.MethodReturnType);
            reason = "Method return type references the delete-class target; propagate to the owning non-private method.";
            return true;
        }

        if (TryResolveInterfaceMethod(typeSyntax, out var interfaceMethod))
        {
            payload = new DeclarationHostPayload(interfaceMethod, DeclarationHostKind.InterfaceMethod);
            reason = "Interface method signature references the delete-class target; propagate to the owning interface method.";
            return true;
        }

        if (TryResolveInterfaceProperty(typeSyntax, out var interfaceProperty))
        {
            payload = new DeclarationHostPayload(interfaceProperty, DeclarationHostKind.InterfaceProperty);
            reason = "Interface property signature references the delete-class target; propagate to the owning interface property.";
            return true;
        }

        if (TryResolveInterfaceEvent(typeSyntax, out var interfaceEvent))
        {
            payload = new DeclarationHostPayload(interfaceEvent, DeclarationHostKind.InterfaceEvent);
            reason = "Interface event signature references the delete-class target; propagate to the owning interface event.";
            return true;
        }

        if (TryResolveInterfaceIndexer(typeSyntax, out var interfaceIndexer))
        {
            payload = new DeclarationHostPayload(interfaceIndexer, DeclarationHostKind.InterfaceIndexer);
            reason = "Interface indexer signature references the delete-class target; propagate to the owning interface indexer.";
            return true;
        }

        if (TryResolveDelegateReturn(typeSyntax, out var delegateDeclaration))
        {
            payload = new DeclarationHostPayload(delegateDeclaration, DeclarationHostKind.DelegateReturnType);
            reason = "Delegate return type references the delete-class target; propagate to the owning delegate declaration.";
            return true;
        }

        if (TryResolveExtensionMethodFromReceiver(typeSyntax, out var extensionMethod))
        {
            payload = new DeclarationHostPayload(extensionMethod, DeclarationHostKind.ExtensionReceiverMethod);
            reason = "Extension method receiver type references the delete-class target; propagate to the owning extension method.";
            return true;
        }

        if (TryResolveBaseDeletionTarget(typeSyntax, out var baseDeletionTarget))
        {
            payload = new DeclarationHostPayload(baseDeletionTarget, DeclarationHostKind.BaseType);
            reason = "Base type references the delete-class target; propagate to the owning base-list deletion host.";
            return true;
        }

        if (TryResolveGenericLocalDeclaration(typeSyntax, out var localDeclaration))
        {
            payload = new DeclarationHostPayload(localDeclaration, DeclarationHostKind.LocalGenericTypeArgument);
            reason = "Local declaration type argument references the delete-class target; propagate to the owning local declaration.";
            return true;
        }

        return false;
    }

    private static bool TryResolveFieldDeclaration(TypeSyntax typeSyntax, out FieldDeclarationSyntax fieldDeclaration)
    {
        fieldDeclaration = typeSyntax.Ancestors()
          .OfType<FieldDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Declaration.Type.Span.Contains(typeSyntax.Span))!;
        return fieldDeclaration is not null;
    }

    private static bool TryResolvePropertyDeclaration(TypeSyntax typeSyntax, out PropertyDeclarationSyntax propertyDeclaration)
    {
        propertyDeclaration = typeSyntax.Ancestors()
          .OfType<PropertyDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span))!;
        return propertyDeclaration is not null &&
          propertyDeclaration.Parent is not InterfaceDeclarationSyntax;
    }

    private static bool TryResolvePrivateMethodReturn(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = typeSyntax.Ancestors()
          .OfType<MethodDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.ReturnType.Span.Contains(typeSyntax.Span))!;
        return methodDeclaration is not null &&
          DeleteClassMethodProposalSafety.IsSafePrivateMethod(methodDeclaration);
    }

    private static bool TryResolveNonPrivateMethodReturn(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = typeSyntax.Ancestors()
          .OfType<MethodDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.ReturnType.Span.Contains(typeSyntax.Span))!;
        return methodDeclaration is not null &&
          DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(methodDeclaration);
    }

    private static bool TryResolveInterfaceMethod(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = typeSyntax.Ancestors()
          .OfType<MethodDeclarationSyntax>()
          .FirstOrDefault(candidate =>
            candidate.ReturnType.Span.Contains(typeSyntax.Span) ||
            candidate.ParameterList.Parameters.Any(parameter => parameter.Type?.Span.Contains(typeSyntax.Span) == true))!;
        return methodDeclaration?.Parent is InterfaceDeclarationSyntax;
    }

    private static bool TryResolveInterfaceProperty(TypeSyntax typeSyntax, out PropertyDeclarationSyntax propertyDeclaration)
    {
        propertyDeclaration = typeSyntax.Ancestors()
          .OfType<PropertyDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span))!;
        return propertyDeclaration?.Parent is InterfaceDeclarationSyntax;
    }

    private static bool TryResolveInterfaceEvent(TypeSyntax typeSyntax, out SyntaxNode eventDeclaration)
    {
        var explicitEvent = typeSyntax.Ancestors()
          .OfType<EventDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span));
        if (explicitEvent?.Parent is InterfaceDeclarationSyntax)
        {
            eventDeclaration = explicitEvent;
            return true;
        }

        var eventField = typeSyntax.Ancestors()
          .OfType<EventFieldDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Declaration.Type.Span.Contains(typeSyntax.Span));
        if (eventField?.Parent is InterfaceDeclarationSyntax)
        {
            eventDeclaration = eventField;
            return true;
        }

        eventDeclaration = typeSyntax;
        return false;
    }

    private static bool TryResolveInterfaceIndexer(TypeSyntax typeSyntax, out IndexerDeclarationSyntax indexerDeclaration)
    {
        indexerDeclaration = typeSyntax.Ancestors()
          .OfType<IndexerDeclarationSyntax>()
          .FirstOrDefault(candidate =>
            candidate.Type.Span.Contains(typeSyntax.Span) ||
            candidate.ParameterList.Parameters.Any(parameter => parameter.Type?.Span.Contains(typeSyntax.Span) == true))!;
        return indexerDeclaration?.Parent is InterfaceDeclarationSyntax;
    }

    private static bool TryResolveDelegateReturn(TypeSyntax typeSyntax, out DelegateDeclarationSyntax delegateDeclaration)
    {
        delegateDeclaration = typeSyntax.Ancestors()
          .OfType<DelegateDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.ReturnType.Span.Contains(typeSyntax.Span))!;
        return delegateDeclaration is not null;
    }

    private static bool TryResolveExtensionMethodFromReceiver(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = null!;
        var parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate =>
            candidate.Type?.Span.Contains(typeSyntax.Span) == true &&
            candidate.Modifiers.Any(SyntaxKind.ThisKeyword));
        if (parameter?.Parent is not ParameterListSyntax parameterList ||
            parameterList.Parameters.FirstOrDefault() != parameter ||
            parameterList.Parent is not MethodDeclarationSyntax method)
        {
            return false;
        }

        methodDeclaration = method;
        return DeleteClassMethodProposalSafety.IsSafeExtensionReceiverMethod(methodDeclaration);
    }

    private static bool TryResolveBaseDeletionTarget(TypeSyntax typeSyntax, out SyntaxNode deletionTarget)
    {
        var simpleBaseType = typeSyntax.Ancestors()
          .OfType<SimpleBaseTypeSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span));
        if (simpleBaseType?.Parent is not BaseListSyntax baseList)
        {
            deletionTarget = typeSyntax;
            return false;
        }

        deletionTarget = baseList.Types.Count == 1
          ? baseList
          : simpleBaseType;
        return true;
    }

    private static bool TryResolveGenericLocalDeclaration(TypeSyntax typeSyntax, out LocalDeclarationStatementSyntax localDeclaration)
    {
        localDeclaration = null!;
        if (!typeSyntax.Ancestors().OfType<TypeArgumentListSyntax>().Any())
        {
            return false;
        }

        var variableDeclaration = typeSyntax.Ancestors()
          .OfType<VariableDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span));
        if (variableDeclaration?.Parent is not LocalDeclarationStatementSyntax statement)
        {
            return false;
        }

        localDeclaration = statement;
        return true;
    }
}
