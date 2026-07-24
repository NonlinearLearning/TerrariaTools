using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes
{
internal sealed class MemberAccessPass : IRoslynCpgPass
{
  internal static MemberAccessPass Instance { get; } = new();

  private MemberAccessPass()
  {
  }

  public string Name => nameof(MemberAccessPass);

  public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
  {
    builder.RunMemberAccessPass(context);
  }
}
}

namespace MinimalRoslynCpg.Builder
{
public sealed partial class RoslynCpgBuilder
{
    internal void RunMemberAccessPass(RoslynCpgBuildContext context)
    {
        foreach (var fieldReferenceOperation in EnumerateOperations(context).OfType<IFieldReferenceOperation>())
        {
            var operationNode = GetOrCreateOperationNode(fieldReferenceOperation, context.Graph);
            AddMemberAccess(
              fieldReferenceOperation,
              operationNode,
              fieldReferenceOperation.Field,
              fieldReferenceOperation.Instance?.Type,
              context.Graph);
        }

        foreach (var propertyReferenceOperation in EnumerateOperations(context).OfType<IPropertyReferenceOperation>())
        {
            var operationNode = GetOrCreateOperationNode(propertyReferenceOperation, context.Graph);
            AddMemberAccess(
              propertyReferenceOperation,
              operationNode,
              propertyReferenceOperation.Property,
              propertyReferenceOperation.Instance?.Type,
              context.Graph);
        }
    }

    private void AddMemberAccess(IOperation operation, RoslynCpgNode operationNode, ISymbol memberSymbol, ITypeSymbol? instanceType, RoslynCpgGraph graph)
    {
        var memberAccessNode = graph.AddNode(new RoslynCpgNode(
          Kind: RoslynCpgNodeKind.MemberAccess,
          DisplayKind: nameof(RoslynCpgNodeKind.MemberAccess),
          Name: memberSymbol.Name,
          FullName: ComposeMemberAccessFullName(memberSymbol, instanceType),
          Signature: ComposeSignature(memberSymbol),
          TypeFullName: ComposeTypeFullName(SymbolTypeOf(memberSymbol)),
          FilePath: operationNode.FilePath,
          SpanStart: operationNode.SpanStart,
          SpanEnd: operationNode.SpanEnd));
        graph.AddEdge(operationNode, memberAccessNode, RoslynCpgEdgeKind.AccessesMember);

        var memberNode = GetOrCreateSymbolNode(memberSymbol, graph);
        graph.AddEdge(memberAccessNode, memberNode, RoslynCpgEdgeKind.Ref);
        AddEvalTypeEdge(memberAccessNode, SymbolTypeOf(memberSymbol), graph);
    }

    private string ComposeMemberAccessFullName(ISymbol memberSymbol, ITypeSymbol? instanceType)
    {
        var baseType = ResolveAccessBaseType(instanceType, memberSymbol);
        if (string.IsNullOrEmpty(baseType))
        {
            return ComposeFullName(memberSymbol);
        }

        return memberSymbol switch
        {
            IPropertySymbol propertySymbol when propertySymbol.Parameters.Length > 0 =>
              $"{baseType}.{propertySymbol.Name}:{ComposePropertySignature(propertySymbol)}",
            _ => $"{baseType}.{memberSymbol.Name}",
        };
    }

    private string ResolveAccessBaseType(ITypeSymbol? instanceType, ISymbol memberSymbol)
    {
        if (instanceType is INamedTypeSymbol namedInstanceType &&
            CanUseReceiverTypeForMemberAccessFullName(namedInstanceType, memberSymbol))
        {
            return ComposeTypeFullName(namedInstanceType);
        }

        return ResolveDeclaringType(instanceType, memberSymbol);
    }

    private string ResolveDeclaringType(ITypeSymbol? instanceType, ISymbol memberSymbol)
    {
        if (instanceType is not null)
        {
            var candidate = ResolveDeclaredType(instanceType, memberSymbol);
            if (candidate is not null)
            {
                return ComposeTypeFullName(candidate);
            }
        }

        return memberSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(memberSymbol.ContainingType);
    }

    private INamedTypeSymbol? ResolveDeclaredType(ITypeSymbol instanceType, ISymbol memberSymbol)
    {
        if (instanceType is not INamedTypeSymbol namedInstanceType)
        {
            return memberSymbol.ContainingType ?? instanceType as INamedTypeSymbol;
        }

        if (memberSymbol.ContainingType is not null &&
            SymbolEqualityComparer.Default.Equals(memberSymbol.ContainingType, namedInstanceType))
        {
            return namedInstanceType;
        }

        var memberName = memberSymbol.Name;
        var exactContainingTypeCandidate = _declaredTypes.FirstOrDefault(declaredType =>
          memberSymbol.ContainingType is not null &&
          SymbolEqualityComparer.Default.Equals(declaredType, memberSymbol.ContainingType));
        if (exactContainingTypeCandidate is not null)
        {
            return exactContainingTypeCandidate;
        }

        var exactReceiverCandidate = _declaredTypes.FirstOrDefault(declaredType =>
          SymbolEqualityComparer.Default.Equals(declaredType, namedInstanceType) &&
          DeclaresCompatibleMember(declaredType, memberSymbol, memberName));
        if (exactReceiverCandidate is not null)
        {
            return exactReceiverCandidate;
        }

        foreach (var declaredType in _declaredTypes)
        {
            if (!InheritsFrom(namedInstanceType, declaredType) ||
                !DeclaresCompatibleMember(declaredType, memberSymbol, memberName))
            {
                continue;
            }

            return declaredType;
        }

        return memberSymbol.ContainingType ?? namedInstanceType;
    }

    private static bool CanUseReceiverTypeForMemberAccessFullName(INamedTypeSymbol instanceType, ISymbol memberSymbol)
    {
        if (memberSymbol.ContainingType is null)
        {
            return true;
        }

        return InheritsFrom(instanceType, memberSymbol.ContainingType) ||
               InheritsFrom(memberSymbol.ContainingType, instanceType);
    }

    private static bool DeclaresCompatibleMember(INamedTypeSymbol declaredType, ISymbol memberSymbol, string memberName)
    {
        foreach (var candidateMember in declaredType.GetMembers(memberName))
        {
            if (candidateMember.Kind != memberSymbol.Kind)
            {
                continue;
            }

            if (MembersMatch(candidateMember, memberSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MembersMatch(ISymbol candidateMember, ISymbol memberSymbol)
    {
        if (SymbolEqualityComparer.Default.Equals(candidateMember, memberSymbol))
        {
            return true;
        }

        return (candidateMember, memberSymbol) switch
        {
            (IPropertySymbol candidateProperty, IPropertySymbol targetProperty) =>
              string.Equals(ComposePropertySignature(candidateProperty), ComposePropertySignature(targetProperty), StringComparison.Ordinal),
            (IFieldSymbol candidateField, IFieldSymbol targetField) =>
              string.Equals(ComposeTypeFullName(candidateField.Type), ComposeTypeFullName(targetField.Type), StringComparison.Ordinal),
            _ => false,
        };
    }
}
}
