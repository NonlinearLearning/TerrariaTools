namespace MinimalRoslynCpg.Model;

/// <summary>
/// Carries the stable identity of an edge execution context such as a callsite.
/// </summary>
public readonly record struct RoslynCpgContextId(string Value)
{
  public override string ToString()
  {
    return Value;
  }
}
