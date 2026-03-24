using ModelPrimitives = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class PlanTargetCompatibilityBuilder
{
    private string _documentPath = "Sample.cs";
    private ModelPrimitives.MemberId _memberId = new("Sample.Player.Run()");
    private ModelPrimitives.MemberKind _memberKind = ModelPrimitives.MemberKind.Method;
    private ModelPrimitives.TargetKind _targetKind = ModelPrimitives.TargetKind.Method;

    /// <summary>
    /// 设置文档路径。
    /// </summary>
    public PlanTargetCompatibilityBuilder WithDocumentPath(string documentPath)
    {
        _documentPath = documentPath;
        return this;
    }

    /// <summary>
    /// 设置成员标识。
    /// </summary>
    public PlanTargetCompatibilityBuilder WithMemberId(string memberId)
    {
        _memberId = new ModelPrimitives.MemberId(memberId);
        return this;
    }

    /// <summary>
    /// 设置成员类型。
    /// </summary>
    public PlanTargetCompatibilityBuilder WithMemberKind(ModelPrimitives.MemberKind memberKind)
    {
        _memberKind = memberKind;
        return this;
    }

    /// <summary>
    /// 设置目标类型。
    /// </summary>
    public PlanTargetCompatibilityBuilder WithTargetKind(ModelPrimitives.TargetKind targetKind)
    {
        _targetKind = targetKind;
        return this;
    }

    /// <summary>
    /// 构建目标标识实例。
    /// </summary>
    public ModelPrimitives.TargetIdentity Build() => new(
        _documentPath,
        _memberId,
        _memberKind,
        _targetKind);
}

