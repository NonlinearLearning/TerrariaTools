using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Runtime.CompilerServices;

namespace NLISSN.Core.Analysis;

public enum UsageProfileStatus { Complete, Truncated, Unavailable, Uncertain }

public sealed record UsageProfileQuery(int MaxCallsites = 1024, int MaxReferences = 4096);

public sealed record SymbolUsageIdentity(string Value);

public sealed record InvocationUsageFact(
  InvocationExpressionSyntax Invocation,
  IInvocationOperation Operation,
  IMethodSymbol TargetMethod,
  string FilePath,
  int SpanStart,
  SymbolUsageIdentity TargetIdentity);

public sealed record SymbolUsageFact(SyntaxNode Syntax, string FilePath, int SpanStart, SymbolUsageIdentity SymbolIdentity);

public enum TypeRelationKind { BaseType, Interface, Override, ExplicitImplementation }

public sealed record TypeRelationFact(
  INamedTypeSymbol Type,
  ISymbol RelatedSymbol,
  TypeRelationKind Kind,
  SymbolUsageIdentity TypeIdentity,
  SymbolUsageIdentity RelatedSymbolIdentity);

public enum DelegateBindingKind { MethodGroup, Lambda, PassThrough, Unsupported }

public sealed record DelegateBindingFact(
  INamedTypeSymbol DelegateType,
  SyntaxNode Binding,
  ISymbol? Target,
  DelegateBindingKind Kind,
  SymbolUsageIdentity DelegateTypeIdentity,
  SymbolUsageIdentity? TargetIdentity);

public sealed record UsageProfileResult<T>(UsageProfileStatus Status, IReadOnlyList<T> Facts, string? Reason = null);

public sealed class SymbolUsageProfile
{
  private static readonly ParameterSymbolComparer ParameterComparer = new();
  private static readonly MethodSymbolComparer MethodComparer = new();
  private static readonly SymbolComparer SymbolComparerInstance = new();
  private readonly Compilation _compilation;
  private readonly Lazy<Index> _index;

  public SymbolUsageProfile(Compilation compilation)
  {
    _compilation = compilation;
    _index = new Lazy<Index>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
  }

  public SymbolUsageIdentity GetSymbolIdentity(ISymbol symbol)
  {
    ArgumentNullException.ThrowIfNull(symbol);
    var definition = symbol.OriginalDefinition;
    var documentationId = DocumentationCommentId.CreateDeclarationId(definition);
    if (documentationId is not null)
    {
      return new SymbolUsageIdentity(documentationId);
    }

    var location = definition.Locations.FirstOrDefault(location => location.IsInSource);
    return location is null
      ? new SymbolUsageIdentity($"metadata:{definition.Kind}")
      : new SymbolUsageIdentity($"source:{definition.Kind}:{location.SourceTree?.FilePath}:{location.SourceSpan.Start}:{location.SourceSpan.Length}");
  }

  public UsageProfileResult<InvocationUsageFact> GetParameterCallsites(IParameterSymbol parameter, UsageProfileQuery? query = null)
  {
    var limit = (query ?? new UsageProfileQuery()).MaxCallsites;
    var facts = _index.Value.ByParameter.TryGetValue(parameter, out var value) ? value : Array.Empty<InvocationUsageFact>();
    if (_index.Value.InvocationUncertaintyReason is not null)
      return new(UsageProfileStatus.Uncertain, Array.Empty<InvocationUsageFact>(), _index.Value.InvocationUncertaintyReason);
    return facts.Count > limit
      ? new(UsageProfileStatus.Truncated, facts.Take(limit).ToArray(), "Callsite budget exceeded.")
      : new(UsageProfileStatus.Complete, facts);
  }

  public UsageProfileResult<InvocationUsageFact> GetMethodCallsites(IMethodSymbol method, UsageProfileQuery? query = null)
  {
    var limit = (query ?? new UsageProfileQuery()).MaxCallsites;
    var facts = _index.Value.ByMethod.TryGetValue(method.OriginalDefinition, out var value) ? value : Array.Empty<InvocationUsageFact>();
    if (_index.Value.InvocationUncertaintyReason is not null)
      return new(UsageProfileStatus.Uncertain, Array.Empty<InvocationUsageFact>(), _index.Value.InvocationUncertaintyReason);
    return facts.Count > limit
      ? new(UsageProfileStatus.Truncated, facts.Take(limit).ToArray(), "Callsite budget exceeded.")
      : new(UsageProfileStatus.Complete, facts);
  }

  public UsageProfileResult<SymbolUsageFact> GetReferences(ISymbol symbol, UsageProfileQuery? query = null)
  {
    var limit = (query ?? new UsageProfileQuery()).MaxReferences;
    var facts = _index.Value.References.TryGetValue(symbol, out var value) ? value : Array.Empty<SymbolUsageFact>();
    return facts.Count > limit
      ? new(UsageProfileStatus.Truncated, facts.Take(limit).ToArray(), "Reference budget exceeded.")
      : new(UsageProfileStatus.Complete, facts);
  }

  public UsageProfileResult<SymbolUsageFact> GetDeclarations(ISymbol symbol)
  {
    var facts = _index.Value.Declarations.TryGetValue(symbol, out var value) ? value : Array.Empty<SymbolUsageFact>();
    return new(UsageProfileStatus.Complete, facts);
  }

  public UsageProfileResult<TypeRelationFact> GetTypeRelations(INamedTypeSymbol type)
  {
    IReadOnlyList<TypeRelationFact> facts = _index.Value.TypeRelations.TryGetValue(type, out var value) ? value : Array.Empty<TypeRelationFact>();
    return new(UsageProfileStatus.Complete, facts);
  }

  public UsageProfileResult<DelegateBindingFact> GetDelegateBindings(INamedTypeSymbol delegateType)
  {
    var facts = _index.Value.DelegateBindings.TryGetValue(delegateType, out var value) ? value : Array.Empty<DelegateBindingFact>();
    return _index.Value.InvocationUncertaintyReason is not null ||
      facts.Any(fact => fact.Kind == DelegateBindingKind.Unsupported || fact.Target is ILocalSymbol)
      ? new(UsageProfileStatus.Uncertain, facts, "Unsupported delegate conversion is present.")
      : new(UsageProfileStatus.Complete, facts);
  }

  private Index Build()
  {
    var byParameter = new Dictionary<IParameterSymbol, List<InvocationUsageFact>>(ParameterComparer);
    var byMethod = new Dictionary<IMethodSymbol, List<InvocationUsageFact>>(MethodComparer);
    var declarations = new Dictionary<ISymbol, List<SymbolUsageFact>>(SymbolComparerInstance);
    var references = new Dictionary<ISymbol, List<SymbolUsageFact>>(SymbolComparerInstance);
    var typeRelations = new Dictionary<INamedTypeSymbol, IReadOnlyList<TypeRelationFact>>(new NamedTypeSymbolComparer());
    var delegateBindings = new Dictionary<INamedTypeSymbol, List<DelegateBindingFact>>(new NamedTypeSymbolComparer());
    var seenDelegateBindings = new HashSet<(INamedTypeSymbol Type, SyntaxNode Syntax)>(new DelegateBindingKeyComparer());
    string? invocationUncertaintyReason = null;
    foreach (var tree in _compilation.SyntaxTrees.OrderBy(tree => tree.FilePath, StringComparer.Ordinal))
    {
      var model = _compilation.GetSemanticModel(tree);
      foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
      {
        var declared = model.GetDeclaredSymbol(node);
        if (declared is not null) Add(declarations, declared, new(node, tree.FilePath, node.SpanStart, GetSymbolIdentity(declared)));
        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
          var referenced = model.GetSymbolInfo(node).Symbol;
          if (referenced is not null) Add(references, referenced, new(node, tree.FilePath, node.SpanStart, GetSymbolIdentity(referenced)));
        }
        if (node is ExpressionSyntax source &&
            IsDelegateValue(source) &&
            TryGetDelegateBindingType(model.GetTypeInfo(source).ConvertedType, out var delegateType, out var isExpressionTree))
        {
          if (!seenDelegateBindings.Add((delegateType, source)))
          {
            continue;
          }
          var binding = isExpressionTree
            ? new DelegateBindingFact(delegateType, source, null, DelegateBindingKind.Unsupported, GetSymbolIdentity(delegateType), null)
            : source switch
          {
            LambdaExpressionSyntax when model.GetOperation(source) is IAnonymousFunctionOperation anonymousFunction => new DelegateBindingFact(delegateType, source, anonymousFunction.Symbol, DelegateBindingKind.Lambda, GetSymbolIdentity(delegateType), GetSymbolIdentity(anonymousFunction.Symbol)),
            _ when model.GetSymbolInfo(source).Symbol is IMethodSymbol method => new DelegateBindingFact(delegateType, source, method, DelegateBindingKind.MethodGroup, GetSymbolIdentity(delegateType), GetSymbolIdentity(method)),
            _ when model.GetSymbolInfo(source).Symbol is IParameterSymbol parameter => new DelegateBindingFact(delegateType, source, parameter, DelegateBindingKind.PassThrough, GetSymbolIdentity(delegateType), GetSymbolIdentity(parameter)),
            _ when model.GetSymbolInfo(source).Symbol is ILocalSymbol local => new DelegateBindingFact(delegateType, source, local, DelegateBindingKind.PassThrough, GetSymbolIdentity(delegateType), GetSymbolIdentity(local)),
            _ => new DelegateBindingFact(delegateType, source, null, DelegateBindingKind.Unsupported, GetSymbolIdentity(delegateType), null)
          };
          if (!delegateBindings.TryGetValue(delegateType, out var bindings)) delegateBindings[delegateType] = bindings = new();
          bindings.Add(binding);
        }
      }
      foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
      {
        var invocationOperation = model.GetOperation(invocation);
        if (invocationOperation is IDynamicInvocationOperation)
        {
          invocationUncertaintyReason = "Dynamic invocation is present in the compilation.";
          continue;
        }
        if (invocationOperation is IInvalidOperation)
        {
          invocationUncertaintyReason = "Unbound invocation is present in the compilation.";
          continue;
        }
        if (invocationOperation is not IInvocationOperation operation) continue;
        if (IsReflectionInvocation(operation))
        {
          invocationUncertaintyReason = "Reflection invocation is present in the compilation.";
        }
        var targetMethod = operation.TargetMethod.OriginalDefinition;
        var fact = new InvocationUsageFact(invocation, operation, targetMethod, tree.FilePath, invocation.SpanStart, GetSymbolIdentity(targetMethod));
        if (!byMethod.TryGetValue(fact.TargetMethod, out var methodFacts)) byMethod[fact.TargetMethod] = methodFacts = new();
        methodFacts.Add(fact);
        foreach (var argument in operation.Arguments)
        {
          if (argument.Parameter is null) continue;
          var parameter = argument.Parameter.OriginalDefinition;
          if (!byParameter.TryGetValue(parameter, out var facts)) byParameter[parameter] = facts = new();
          facts.Add(fact);
        }
      }
    }
    var frozen = new Dictionary<IParameterSymbol, IReadOnlyList<InvocationUsageFact>>(ParameterComparer);
    var frozenMethods = new Dictionary<IMethodSymbol, IReadOnlyList<InvocationUsageFact>>(MethodComparer);
    foreach (var pair in byParameter)
    {
      frozen[pair.Key] = pair.Value.OrderBy(f => f.FilePath, StringComparer.Ordinal)
        .ThenBy(f => f.SpanStart).ToArray();
    }
    foreach (var pair in byMethod)
    {
      frozenMethods[pair.Key] = pair.Value.OrderBy(f => f.FilePath, StringComparer.Ordinal)
        .ThenBy(f => f.SpanStart).ToArray();
    }
    foreach (var type in EnumerateTypes(_compilation.Assembly.GlobalNamespace))
    {
      var relations = new List<TypeRelationFact>();
      var typeIdentity = GetSymbolIdentity(type);
      if (type.BaseType is not null) relations.Add(new(type, type.BaseType, TypeRelationKind.BaseType, typeIdentity, GetSymbolIdentity(type.BaseType)));
      relations.AddRange(type.AllInterfaces.Select(@interface => new TypeRelationFact(type, @interface, TypeRelationKind.Interface, typeIdentity, GetSymbolIdentity(@interface))));
      foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
      {
        if (method.OverriddenMethod is not null) relations.Add(new(type, method.OverriddenMethod, TypeRelationKind.Override, typeIdentity, GetSymbolIdentity(method.OverriddenMethod)));
        relations.AddRange(method.ExplicitInterfaceImplementations.Select(implementation => new TypeRelationFact(type, implementation, TypeRelationKind.ExplicitImplementation, typeIdentity, GetSymbolIdentity(implementation))));
      }
      typeRelations[type] = relations
        .OrderBy(relation => relation.Kind)
        .ThenBy(relation => relation.RelatedSymbolIdentity.Value, StringComparer.Ordinal)
        .ToArray();
    }
    var frozenBindings = delegateBindings.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<DelegateBindingFact>)pair.Value.OrderBy(fact => fact.Binding.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(fact => fact.Binding.SpanStart).ToArray(),
      new NamedTypeSymbolComparer());
    return new(
      frozen,
      frozenMethods,
      Freeze(declarations),
      Freeze(references),
      typeRelations,
      frozenBindings,
      invocationUncertaintyReason);
  }

  private sealed record Index(
    IReadOnlyDictionary<IParameterSymbol, IReadOnlyList<InvocationUsageFact>> ByParameter,
    IReadOnlyDictionary<IMethodSymbol, IReadOnlyList<InvocationUsageFact>> ByMethod,
    IReadOnlyDictionary<ISymbol, IReadOnlyList<SymbolUsageFact>> Declarations,
    IReadOnlyDictionary<ISymbol, IReadOnlyList<SymbolUsageFact>> References,
    IReadOnlyDictionary<INamedTypeSymbol, IReadOnlyList<TypeRelationFact>> TypeRelations,
    IReadOnlyDictionary<INamedTypeSymbol, IReadOnlyList<DelegateBindingFact>> DelegateBindings,
    string? InvocationUncertaintyReason);

  private sealed class ParameterSymbolComparer : IEqualityComparer<IParameterSymbol>
  {
    public bool Equals(IParameterSymbol? x, IParameterSymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);

    public int GetHashCode(IParameterSymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
  }

  private sealed class MethodSymbolComparer : IEqualityComparer<IMethodSymbol>
  {
    public bool Equals(IMethodSymbol? x, IMethodSymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);

    public int GetHashCode(IMethodSymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
  }

  private static void Add<T>(Dictionary<ISymbol, List<T>> values, ISymbol symbol, T fact)
  {
    if (!values.TryGetValue(symbol, out var facts)) values[symbol] = facts = new();
    facts.Add(fact);
  }

  private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol currentNamespace)
  {
    foreach (var type in currentNamespace.GetTypeMembers()) yield return type;
    foreach (var nestedNamespace in currentNamespace.GetNamespaceMembers())
    {
      foreach (var type in EnumerateTypes(nestedNamespace)) yield return type;
    }
  }

  private static bool IsDelegateValue(ExpressionSyntax expression)
  {
    return expression.Parent switch
    {
      EqualsValueClauseSyntax equalsValue => ReferenceEquals(equalsValue.Value, expression),
      AssignmentExpressionSyntax assignment => ReferenceEquals(assignment.Right, expression),
      ArgumentSyntax argument => ReferenceEquals(argument.Expression, expression),
      ReturnStatementSyntax returnStatement => ReferenceEquals(returnStatement.Expression, expression),
      _ => false
    };
  }

  private static bool TryGetDelegateBindingType(
    ITypeSymbol? convertedType,
    out INamedTypeSymbol delegateType,
    out bool isExpressionTree)
  {
    if (convertedType is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Delegate)
    {
      delegateType = namedType;
      isExpressionTree = false;
      return true;
    }

    if (convertedType is INamedTypeSymbol expressionTree &&
        expressionTree.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>" &&
        expressionTree.TypeArguments.SingleOrDefault() is INamedTypeSymbol expressionDelegate &&
        expressionDelegate.TypeKind == TypeKind.Delegate)
    {
      delegateType = expressionDelegate;
      isExpressionTree = true;
      return true;
    }

    delegateType = null!;
    isExpressionTree = false;
    return false;
  }

  private static bool IsReflectionInvocation(IInvocationOperation operation)
  {
    var containingType = operation.TargetMethod.ContainingType?.ToDisplayString();
    return (containingType is "System.Type" or "System.Reflection.MethodBase" or "System.Delegate") &&
      operation.TargetMethod.Name is "GetMethod" or "GetMethods" or "Invoke" or "InvokeMember" or "DynamicInvoke";
  }

  private static IReadOnlyDictionary<ISymbol, IReadOnlyList<SymbolUsageFact>> Freeze(Dictionary<ISymbol, List<SymbolUsageFact>> values)
  {
    return values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SymbolUsageFact>)pair.Value.OrderBy(fact => fact.FilePath, StringComparer.Ordinal).ThenBy(fact => fact.SpanStart).ToArray(), SymbolComparerInstance);
  }

  private sealed class SymbolComparer : IEqualityComparer<ISymbol>
  {
    public bool Equals(ISymbol? x, ISymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);
    public int GetHashCode(ISymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
  }

  private sealed class NamedTypeSymbolComparer : IEqualityComparer<INamedTypeSymbol>
  {
    public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);
    public int GetHashCode(INamedTypeSymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
  }

  private sealed class DelegateBindingKeyComparer : IEqualityComparer<(INamedTypeSymbol Type, SyntaxNode Syntax)>
  {
    public bool Equals((INamedTypeSymbol Type, SyntaxNode Syntax) x, (INamedTypeSymbol Type, SyntaxNode Syntax) y) =>
      SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && ReferenceEquals(x.Syntax, y.Syntax);
    public int GetHashCode((INamedTypeSymbol Type, SyntaxNode Syntax) value) =>
      HashCode.Combine(SymbolEqualityComparer.Default.GetHashCode(value.Type), RuntimeHelpers.GetHashCode(value.Syntax));
  }
}
