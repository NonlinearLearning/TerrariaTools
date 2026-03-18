using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native target identities.
/// </summary>
public sealed class PlanTargetCompatibilityBuilder
{
    private string _documentPath = "Sample.cs";
    private ModelPrimitives.MemberId _memberId = new("Sample.Player.Run()");
    private ModelPrimitives.MemberKind _memberKind = ModelPrimitives.MemberKind.Method;
    private ModelPrimitives.TargetKind _targetKind = ModelPrimitives.TargetKind.Method;

    public PlanTargetCompatibilityBuilder WithDocumentPath(string documentPath)
    {
        _documentPath = documentPath;
        return this;
    }

    public PlanTargetCompatibilityBuilder WithMemberId(string memberId)
    {
        _memberId = new ModelPrimitives.MemberId(memberId);
        return this;
    }

    public PlanTargetCompatibilityBuilder WithMemberKind(ModelPrimitives.MemberKind memberKind)
    {
        _memberKind = memberKind;
        return this;
    }

    public PlanTargetCompatibilityBuilder WithTargetKind(ModelPrimitives.TargetKind targetKind)
    {
        _targetKind = targetKind;
        return this;
    }

    public ModelPrimitives.TargetIdentity Build() => new(
        _documentPath,
        _memberId,
        _memberKind,
        _targetKind);
}
