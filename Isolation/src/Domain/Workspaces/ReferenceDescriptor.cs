namespace Domain.Workspaces;

/// <summary>
/// 描述工作区中的外部引用。
/// </summary>
/// <param name="Name">引用名称。</param>
/// <param name="Version">引用版本。</param>
public sealed record ReferenceDescriptor(string Name, string Version);
