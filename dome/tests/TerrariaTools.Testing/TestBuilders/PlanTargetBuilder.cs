using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class PlanTargetBuilder
{
    private string _documentPath = "Sample.cs";
    private MemberId _memberId = new("Sample.Player.Run()");
    private MemberKind _memberKind = MemberKind.Method;
    private TargetKind _targetKind = TargetKind.Method;
    private int _spanStart;
    private int _spanLength = 1;
    private string _displayText = "Run";
    private TargetResolutionKey? _resolutionKey;

    public PlanTargetBuilder WithDocumentPath(string documentPath)
    {
        _documentPath = documentPath;
        return this;
    }

    public PlanTargetBuilder WithMemberId(string memberId)
    {
        _memberId = new MemberId(memberId);
        return this;
    }

    public PlanTargetBuilder WithMemberKind(MemberKind memberKind)
    {
        _memberKind = memberKind;
        return this;
    }

    public PlanTargetBuilder WithTargetKind(TargetKind targetKind)
    {
        _targetKind = targetKind;
        return this;
    }

    public PlanTargetBuilder WithSpan(int spanStart, int spanLength)
    {
        _spanStart = spanStart;
        _spanLength = spanLength;
        return this;
    }

    public PlanTargetBuilder WithDisplayText(string displayText)
    {
        _displayText = displayText;
        return this;
    }

    public PlanTargetBuilder WithResolutionKey(TargetResolutionKey? resolutionKey)
    {
        _resolutionKey = resolutionKey;
        return this;
    }

    public PlanTarget Build() => new(
        _documentPath,
        _memberId,
        _memberKind,
        _targetKind,
        _spanStart,
        _spanLength,
        _displayText,
        _resolutionKey);
}
