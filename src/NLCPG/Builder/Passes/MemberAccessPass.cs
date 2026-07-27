using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class MemberAccessPass : INLCPGPass
    {
        internal static MemberAccessPass Instance { get; } = new();

        private MemberAccessPass()
        {
        }

        public string Name => nameof(MemberAccessPass);

        // 触发成员访问 pass，为字段和属性访问补成员节点与引用边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunMemberAccessPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        internal void RunMemberAccessPass(NLCPGBuildContext context)
        {
            // 字段引用直接映射到字段符号。
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

            // 属性引用同样走成员访问图，但目标符号换成属性。
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

        private void AddMemberAccess(IOperation operation, NLCPGNode operationNode, ISymbol memberSymbol, ITypeSymbol? instanceType, NLCPGGraph graph)
        {
            var memberAccessNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.MemberAccess,
              DisplayKind: nameof(NLCPGNodeKind.MemberAccess),
              Name: memberSymbol.Name,
              FullName: ComposeMemberAccessFullName(memberSymbol, instanceType),
              Signature: ComposeSignature(memberSymbol),
              TypeFullName: ComposeTypeFullName(SymbolTypeOf(memberSymbol)),
              FilePath: operationNode.FilePath,
              SpanStart: operationNode.SpanStart,
              SpanEnd: operationNode.SpanEnd));
            graph.AddEdge(operationNode, memberAccessNode, NLCPGEdgeKind.AccessesMember);

            var memberNode = GetOrCreateSymbolNode(memberSymbol, graph);
            graph.AddEdge(memberAccessNode, memberNode, NLCPGEdgeKind.Ref);
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
            // 先尝试命中符号原始声明类型，避免后续按继承关系误选更宽泛的类型。
            var exactContainingTypeCandidate = _declaredTypes.FirstOrDefault(declaredType =>
              memberSymbol.ContainingType is not null &&
              SymbolEqualityComparer.Default.Equals(declaredType, memberSymbol.ContainingType));
            if (exactContainingTypeCandidate is not null)
            {
                return exactContainingTypeCandidate;
            }

            // 再尝试接收者本身是否就是声明了兼容成员的内部类型。
            var exactReceiverCandidate = _declaredTypes.FirstOrDefault(declaredType =>
              SymbolEqualityComparer.Default.Equals(declaredType, namedInstanceType) &&
              DeclaresCompatibleMember(declaredType, memberSymbol, memberName));
            if (exactReceiverCandidate is not null)
            {
                return exactReceiverCandidate;
            }

            // 最后沿工程内继承关系找第一个兼容宿主。
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
