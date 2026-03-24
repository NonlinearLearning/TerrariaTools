using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 提供常用函数图请求的工厂方法。
/// </summary>
public static class FunctionGraphRequests
{
    /// <summary>
    /// 创建覆盖整项目调用关系的函数图请求。
    /// </summary>
    /// <param name="requester">请求方标识。</param>
    /// <param name="reason">请求原因。</param>
    /// <returns>整项目调用图请求。</returns>
    public static CoreAnalysis.FunctionGraphRequest WholeProjectCalls(string requester, string reason) =>
        new(
            CoreAnalysis.FunctionGraphScope.WholeProject,
            Array.Empty<CoreCommon.MemberId>(),
            0,
            [CoreCommon.FunctionDependencyKind.Calls],
            requester,
            reason);

    /// <summary>
    /// 创建从指定成员集合向外扩展一层调用关系的函数图请求。
    /// </summary>
    /// <param name="rootMemberIds">扩展起点成员集合。</param>
    /// <param name="requester">请求方标识。</param>
    /// <param name="reason">请求原因。</param>
    /// <returns>按成员扩展的调用图请求。</returns>
    public static CoreAnalysis.FunctionGraphRequest ExpandedMembersCalls(
        IReadOnlyList<CoreCommon.MemberId> rootMemberIds,
        string requester,
        string reason) =>
        new(
            CoreAnalysis.FunctionGraphScope.ExpandedMembers,
            rootMemberIds,
            1,
            [CoreCommon.FunctionDependencyKind.Calls],
            requester,
            reason);
}
