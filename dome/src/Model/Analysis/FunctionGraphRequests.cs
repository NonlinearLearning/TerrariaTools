using TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Model.Analysis;

public static class FunctionGraphRequests
{
    public static FunctionGraphRequest WholeProjectCalls(string requester, string reason) =>
        new(
            FunctionGraphScope.WholeProject,
            Array.Empty<MemberId>(),
            0,
            [FunctionDependencyKind.Calls],
            requester,
            reason);

    public static FunctionGraphRequest ExpandedMembersCalls(
        IReadOnlyList<MemberId> rootMemberIds,
        string requester,
        string reason) =>
        new(
            FunctionGraphScope.ExpandedMembers,
            rootMemberIds,
            1,
            [FunctionDependencyKind.Calls],
            requester,
            reason);
}
