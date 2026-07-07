using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynPrototype.Propagation;

public enum MethodParameterUsageMode
{
    NamedArgument,
    Optional,
    ParamsOmitted,
    PrivatePositional,
    PublicPositional
}

public sealed record MethodParameterUsagePayload(
  MethodDeclarationSyntax Method,
  ParameterSyntax Parameter,
  int ParameterIndex,
  MethodParameterUsageMode Mode,
  IReadOnlyList<InvocationExpressionSyntax> InvocationCallsites);

public enum LocalFunctionParameterUsageMode
{
    NamedArgument,
    Optional,
    Positional
}

public sealed record LocalFunctionParameterUsagePayload(
  LocalFunctionStatementSyntax LocalFunction,
  ParameterSyntax Parameter,
  int ParameterIndex,
  LocalFunctionParameterUsageMode Mode,
  IReadOnlyList<InvocationExpressionSyntax> InvocationCallsites);

public enum IndexerParameterUsageMode
{
    NamedArgument,
    Positional
}

public sealed record IndexerParameterUsagePayload(
  IndexerDeclarationSyntax Indexer,
  ParameterSyntax Parameter,
  int ParameterIndex,
  IndexerParameterUsageMode Mode,
  IReadOnlyList<ElementAccessExpressionSyntax> AccessCallsites);

public enum DelegateUsageMode
{
    InvocationChain,
    Lambda,
    MethodGroup,
    PlainSignature
}

public sealed record DelegateUsagePayload(
  DelegateDeclarationSyntax DelegateDeclaration,
  ParameterSyntax Parameter,
  int ParameterIndex,
  DelegateUsageMode Mode,
  IReadOnlyList<MethodDeclarationSyntax> MethodTargets,
  IReadOnlyList<LocalFunctionStatementSyntax> LocalFunctionTargets,
  IReadOnlyList<ExpressionSyntax> LambdaTargets,
  IReadOnlyList<InvocationExpressionSyntax> InvocationCallsites);

public sealed record ExtensionMethodMappedCallsitePayload(
  MethodDeclarationSyntax Method,
  ParameterSyntax Parameter,
  int ParameterIndex,
  IReadOnlyList<InvocationExpressionSyntax> InvocationCallsites);

public enum DeclarationHostKind
{
    BaseType,
    DelegateReturnType,
    ExtensionReceiverMethod,
    FieldDeclaration,
    InterfaceEvent,
    InterfaceIndexer,
    InterfaceMethod,
    InterfaceProperty,
    LocalGenericTypeArgument,
    MethodReturnType,
    PropertyDeclaration
}

public sealed record DeclarationHostPayload(
  SyntaxNode HostDeclaration,
  DeclarationHostKind Kind);
