namespace TerrariaTools.Dome.Core.Cpg;

public sealed class FileCreationPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        bool hasFileNode = Context.Cpg.Nodes.OfType<FileNode>().Any();
        if (!hasFileNode)
        {
            string fileName = Context.Cpg.FrontendContext?.Config.FileName ?? "input.cs";
            diff.AddNode(new FileNode($"file:{fileName}", fileName));
        }
    }
}
