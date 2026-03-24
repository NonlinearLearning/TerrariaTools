namespace TerrariaTools.Dome.Core.Cpg;

public sealed class MethodStubCreatorPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        HashSet<string> existingMethodIds = Context.Cpg.Nodes
            .OfType<MethodNode>()
            .Select(node => node.Id)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (CallNode call in Context.Cpg.Nodes.OfType<CallNode>())
        {
            string targetMethodId = GetTargetMethodId(call);
            if (string.IsNullOrWhiteSpace(call.TargetMethodName) || existingMethodIds.Contains(targetMethodId))
            {
                continue;
            }

            diff.AddNode(
                new MethodNode(
                    targetMethodId,
                    call.TargetMethodName,
                    GetContainingTypeName(targetMethodId),
                    fullName: call.MethodFullName));
            existingMethodIds.Add(targetMethodId);
        }
    }

    private static string GetTargetMethodId(CallNode call)
    {
        if (!string.IsNullOrWhiteSpace(call.ResolvedTargetMethodId))
        {
            return call.ResolvedTargetMethodId;
        }

        return $"method:{call.TargetMethodName}";
    }

    private static string? GetContainingTypeName(string targetMethodId)
    {
        const string prefix = "method:";
        if (!targetMethodId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string suffix = targetMethodId[prefix.Length..];
        int separatorIndex = suffix.LastIndexOf('.');
        return separatorIndex > 0 ? suffix[..separatorIndex] : null;
    }
}
