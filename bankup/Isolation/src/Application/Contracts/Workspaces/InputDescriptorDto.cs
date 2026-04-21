using Application.Contracts;

namespace Application.Contracts.Workspaces;

/// <summary>
/// 输入描述 DTO。
/// </summary>
public sealed class InputDescriptorDto
{
    public ContractInputOrigin Origin { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public ContractRunMode RunMode { get; init; }
}
