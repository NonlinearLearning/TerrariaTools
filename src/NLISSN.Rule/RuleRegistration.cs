namespace NLISSN.Core.Pipeline;

/// <summary>
/// Marks a concrete stage rule for inclusion in the compile-time generated catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleRegistrationAttribute : Attribute
{
}

/// <summary>
/// Explicitly excludes an intentional concrete rule from the generated catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleCatalogIgnoreAttribute : Attribute
{
}
