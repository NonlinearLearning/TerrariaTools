using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 收敛配置文件节点的属性装配规则。
/// </summary>
public static class ConfigFileNodeConventions
{
    /// <summary>
    /// 将配置文件属性写入节点。
    /// </summary>
    public static void ApplyConfigFileProperties(CpgNode node, string filePath, string content, long astParentId)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Name", Path.GetFileName(filePath));
        node.SetProperty("FileName", filePath);
        node.SetProperty("FullName", filePath);
        node.SetProperty("Content", content);
        node.SetProperty("AstParentId", astParentId);
    }
}
