namespace NLISSN.Artifacts;

/// <summary>
/// Defines the stable audit categories used to partition rewrite diffs.
/// </summary>
internal enum RuleDiffCategory
{
    TypeDeclaration,
    ExpressionControlFlow,
    ParameterShrink,
    MethodGlobal,
    InterfaceVisibility,
}

/// <summary>
/// Resolves every proposal rule ID to its explicit diff artifact category.
/// </summary>
internal static class RuleDiffCategoryRegistry
{
    private static readonly IReadOnlyDictionary<string, RuleDiffCategory> Categories =
      new Dictionary<string, RuleDiffCategory>(StringComparer.Ordinal)
      {
          ["propose.default-removal"] = RuleDiffCategory.ExpressionControlFlow,

          ["propose.type.type-syntax-declaration"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.method-return-type"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.public-method-return-type"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.interface-method"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.interface-property"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.interface-event"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.interface-indexer"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.delegate"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.extension-receiver"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.base-type"] = RuleDiffCategory.TypeDeclaration,
          ["propose.type.generic-type-argument"] = RuleDiffCategory.TypeDeclaration,

          ["propose.logical-expression"] = RuleDiffCategory.ExpressionControlFlow,
          ["propose.if-structure"] = RuleDiffCategory.ExpressionControlFlow,
          ["propose.control-structure-removal"] = RuleDiffCategory.ExpressionControlFlow,

          ["propose.type.parameter"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.private-method-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.named-argument-method-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.optional-parameter-defaulted-method-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.public-parameter"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.params-method-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.public-method-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.named-argument-local-function-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.optional-parameter-defaulted-local-function-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.local-function-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.named-argument-indexer-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.indexer-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.delegate-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.method-group-delegate-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.lambda-delegate-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.delegate-invocation-chain-parameter-shrink"] = RuleDiffCategory.ParameterShrink,
          ["propose.type.extension-receiver-non-first-parameter-shrink"] = RuleDiffCategory.ParameterShrink,

          ["propose.unreachable-method"] = RuleDiffCategory.MethodGlobal,
          ["propose.unreferenced-method"] = RuleDiffCategory.MethodGlobal,

          ["propose.clear-unused-interface-implementation"] = RuleDiffCategory.InterfaceVisibility,
          ["propose.privatize-internal-only-public-method"] = RuleDiffCategory.InterfaceVisibility,
      };

    internal static RuleDiffCategory Resolve(string ruleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (Categories.TryGetValue(ruleId, out var category))
        {
            return category;
        }

        throw new InvalidOperationException($"No diff category is registered for proposal rule '{ruleId}'.");
    }
}
