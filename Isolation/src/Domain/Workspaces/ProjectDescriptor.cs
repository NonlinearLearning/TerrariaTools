namespace Domain.Workspaces;

/// <summary>
/// 描述工作区中的项目。
/// </summary>
/// <param name="Name">项目名称。</param>
/// <param name="Path">项目路径。</param>
public sealed record ProjectDescriptor(string Name, string Path);
