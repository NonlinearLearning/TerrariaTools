using TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Core.Analysis;

/// <summary>
/// 提供函数图查询请求的常用工厂方法。
/// </summary>
public static class FunctionGraphRequests
{
    /// <summary>
    /// 创建覆盖整个项目的调用图请求。
    /// </summary>
    /// <param name="requester">发起请求的组件名称。</param>
    /// <param name="reason">发起请求的原因说明。</param>
    /// <returns>只包含调用边的整项目函数图请求。</returns>
    public static FunctionGraphRequest WholeProjectCalls(string requester, string reason) =>
        new(
            FunctionGraphScope.WholeProject,
            Array.Empty<MemberId>(),
            0,
            [FunctionDependencyKind.Calls],
            requester,
            reason);

    /// <summary>
    /// 创建从指定根成员向外展开的调用图请求。
    /// </summary>
    /// <param name="rootMemberIds">作为展开起点的根成员集合。</param>
    /// <param name="requester">发起请求的组件名称。</param>
    /// <param name="reason">发起请求的原因说明。</param>
    /// <returns>只包含调用边的成员展开函数图请求。</returns>
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
