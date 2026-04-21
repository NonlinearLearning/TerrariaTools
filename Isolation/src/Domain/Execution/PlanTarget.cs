using Domain.Workspaces;

namespace Domain.Execution;

/// <summary>
/// 表示计划目标。
/// </summary>
public sealed class PlanTarget
{
    public PlanTarget(DocumentPath documentPath, string targetName, string? memberSignature, string? anchorText)
        : this(
            documentPath,
            Domain.Common.TargetName.Create(targetName),
            Domain.Common.MemberSignature.CreateNullable(memberSignature),
            anchorText)
    {
    }

    public PlanTarget(
        DocumentPath documentPath,
        Domain.Common.TargetName targetName,
        Domain.Common.MemberSignature? memberSignature,
        string? anchorText)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        DocumentPath = documentPath;
        TargetNameValue = targetName;
        MemberSignatureValue = memberSignature;
        AnchorText = anchorText?.Trim();
    }

    public DocumentPath DocumentPath { get; }

    public string TargetName => TargetNameValue.Value;

    public Domain.Common.TargetName TargetNameValue { get; }

    public string? MemberSignature => MemberSignatureValue?.Value;

    public Domain.Common.MemberSignature? MemberSignatureValue { get; }

    public string? AnchorText { get; }
}
