namespace NLCPG.Builder.Passes;

//事情开始变得神秘起来了
internal sealed class InterproceduralDataFlowPass : INLCPGPass
{
    internal static InterproceduralDataFlowPass Instance { get; } = new();

    private InterproceduralDataFlowPass()
    {
    }

    public string Name => nameof(InterproceduralDataFlowPass);

    // 触发跨过程数据流 pass，补方法边界之间的桥接边。
    public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
    {
        builder.RunInterproceduralDataFlowPass(context);
    }
}
