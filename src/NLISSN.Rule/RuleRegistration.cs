namespace NLISSN.Core.Pipeline;

/// <summary>
/// Selects the optional rule family that owns a stage rule.
/// </summary>
public enum RuleFeature
{
    Core = 0,
    UnreachableMethodDeletion = 1,
    UnreferencedMethodDeletion = 2,
    UnusedInterfaceImplementationCleanup = 3,
    InternalOnlyPublicMethodPrivatization = 4,
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleRegistrationAttribute : Attribute
{
    /// <summary>
    /// Registers a core rule for compatibility with the original marker syntax.
    /// </summary>
    public RuleRegistrationAttribute()
      : this(RuleFeature.Core)
    {
    }

    public RuleRegistrationAttribute(RuleFeature feature)
    {
        Feature = feature;
    }

    public RuleFeature Feature { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleCatalogIgnoreAttribute : Attribute
{
}
