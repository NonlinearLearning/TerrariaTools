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
          ["DEL-SOBJ-PROPOSE-DEFAULT-001"] = RuleDiffCategory.ExpressionControlFlow,

          ["DEL-CLASS-PROP-TYPE-DECL-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-RETURN-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-PUBLIC-RETURN-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-IFACE-METHOD-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-IFACE-PROPERTY-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-IFACE-EVENT-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-IFACE-INDEXER-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-DELEGATE-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-EXT-RECV-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-BASE-001"] = RuleDiffCategory.TypeDeclaration,
          ["DEL-CLASS-PROP-GENERIC-001"] = RuleDiffCategory.TypeDeclaration,

          ["DEL-SOBJ-PROPOSE-LOGIC-001"] = RuleDiffCategory.ExpressionControlFlow,
          ["DEL-SOBJ-PROPOSE-IF-001"] = RuleDiffCategory.ExpressionControlFlow,
          ["DEL-SOBJ-PROPOSE-CTRL-001"] = RuleDiffCategory.ExpressionControlFlow,

          ["DEL-CLASS-PROP-PARAM-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-PRIVATE-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-NAMED-METHOD-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-OPTIONAL-METHOD-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-PUBLIC-PARAM-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-PARAMS-METHOD-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-PUBLIC-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-NAMED-LOCALFUNC-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-OPTIONAL-LOCALFUNC-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-LOCALFUNC-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-NAMED-INDEXER-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-INDEXER-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-DELEGATE-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-METHODGROUP-DELEGATE-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-LAMBDA-DELEGATE-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-DELEGATE-INVOKE-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,
          ["DEL-CLASS-PROP-EXT-NONRECV-PARAM-SHRINK-001"] = RuleDiffCategory.ParameterShrink,

          ["DEL-DEAD-001"] = RuleDiffCategory.MethodGlobal,
          ["DEL-UNREF-METHOD-PROP-001"] = RuleDiffCategory.MethodGlobal,

          ["CLR-UNUSED-IFACE-IMPL-PROP-001"] = RuleDiffCategory.InterfaceVisibility,
          ["PRIV-INTERNAL-PUBLIC-PROP-001"] = RuleDiffCategory.InterfaceVisibility,
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
