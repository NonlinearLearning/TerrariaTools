namespace MinimalRoslynCpg.Builder.Passes;

internal interface IRoslynCpgPass
{
  string Name { get; }

  void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context);
}
