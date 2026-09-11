namespace NLISSN.Core.Pipeline;

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
