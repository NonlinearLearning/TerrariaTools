using NLCPG.Contracts;
using NLCPG.ProjectJson;
using Xunit;

namespace NLISSN.Tests.ProjectJson;

/// <summary>
/// 锁定「额外请求的能力位必须并入 <see cref="NLCPGCapability.Default"/>」这一语义。
/// </summary>
/// <remarks>
/// 背景（真实缺陷，已由端到端实测坐实）：<c>NLCPGBuilder.ResolveCapabilityBuildPlan</c>
/// 对非 <c>null</c> 的 <c>RequestedCapabilities</c> 走 <c>Aggregate(None, OR)</c>，
/// 是**替换**而非并入默认集。故若把 <c>["InterproceduralDataFlow"]</c> 直接透传给 builder，
/// 产出的图会丢掉全部 <c>Default</c> 位对应的边，反而比不开该开关更贫瘠。
/// 实测（Terraria/Collision.cs，DOP8）：SyntaxToken 28109→0、Reference 9829→0、
/// TypeRef 8043→0、CfgNext 638→0，而 InterproceduralDataFlow 0→10126。
/// 本测试直接断言生产实现的合并函数，避免该缺陷再次溜过。
/// </remarks>
public sealed class ProjectExportCapabilityMergeTests
{
    [Fact]
    public void MergeRequestedCapabilities_AddsExtraCapabilityOnTopOfDefault()
    {
        var merged = ProjectJsonExporter.MergeRequestedCapabilities(
          new[] { NLCPGCapability.InterproceduralDataFlow });

        var result = Assert.Single(merged!);
        Assert.True(
          (result & NLCPGCapability.InterproceduralDataFlow) != 0,
          "额外请求的能力位必须存在。");
        // 逐位断言 Default 的全部成员都还在——这是本测试的核心。
        Assert.True(
          (result & NLCPGCapability.Default) == NLCPGCapability.Default,
          $"Default 的每一位都必须保留，实际为 {result}。");
    }

    [Fact]
    public void MergeRequestedCapabilities_WithoutExtraCapabilities_ReturnsNullNotEmpty()
    {
        // null 才会让 builder 走 NLCPGCapability.Default；空数组会被折叠成 None，
        // 产出几乎无边的图。故这里必须断言 null 而不是「空集合」。
        Assert.Null(ProjectJsonExporter.MergeRequestedCapabilities(null));
        Assert.Null(ProjectJsonExporter.MergeRequestedCapabilities(Array.Empty<NLCPGCapability>()));
    }

    [Fact]
    public void MergeRequestedCapabilities_KeepsEveryDefaultBit_ForEveryExtraBit()
    {
        // 对每个非 Default 能力位逐一验证，防止只对 InterproceduralDataFlow 特判。
        var extras = new[]
        {
            NLCPGCapability.InterproceduralDataFlow,
            NLCPGCapability.Dominance,
            NLCPGCapability.ControlDependence,
        };

        foreach (var extra in extras)
        {
            var merged = ProjectJsonExporter.MergeRequestedCapabilities(new[] { extra });
            var result = Assert.Single(merged!);
            Assert.True((result & extra) != 0, $"{extra} 必须存在。");
            Assert.True(
              (result & NLCPGCapability.Default) == NLCPGCapability.Default,
              $"请求 {extra} 时 Default 的每一位都必须保留，实际为 {result}。");
        }
    }

    [Fact]
    public void MergeRequestedCapabilities_DeduplicatesRepeatedRequests()
    {
        var merged = ProjectJsonExporter.MergeRequestedCapabilities(
          new[] { NLCPGCapability.InterproceduralDataFlow, NLCPGCapability.InterproceduralDataFlow });

        var result = Assert.Single(merged!);
        Assert.True((result & NLCPGCapability.InterproceduralDataFlow) != 0);
    }
}
