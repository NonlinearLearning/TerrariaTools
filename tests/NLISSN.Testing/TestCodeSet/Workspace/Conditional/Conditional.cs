namespace Workspace.Conditional;

public static class ConditionalValue
{
#if WORKSPACE_DEBUG
  public const string Configuration = "Debug";
#else
  public const string Configuration = "Release";
#endif
}
