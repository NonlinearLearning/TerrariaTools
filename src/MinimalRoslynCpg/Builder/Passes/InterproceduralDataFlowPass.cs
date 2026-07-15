namespace MinimalRoslynCpg.Builder.Passes;

internal sealed class InterproceduralDataFlowPass : IRoslynCpgPass
{
  internal static InterproceduralDataFlowPass Instance { get; } = new();

  private InterproceduralDataFlowPass()
  {
  }

  public string Name => nameof(InterproceduralDataFlowPass);

  public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
  {
    builder.RunInterproceduralDataFlowPass(context);
  }
}
