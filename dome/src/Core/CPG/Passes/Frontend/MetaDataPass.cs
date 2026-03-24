namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MetaDataPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        diff.AddNode(new MetaDataNode("meta-data", "CSHARP", frontendContext.Config.FileName, "0.1"));
    }
}
