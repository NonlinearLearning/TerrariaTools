using Domain.Propagation;

namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示影子类结果。
/// </summary>
public sealed class ShadowClass
{
    private readonly List<string> memberNames = new();

    private ShadowClass(ShadowClassArtifact artifact)
    {
        Artifact = artifact;
        Boundary = ShadowBoundary.Create();
    }

    public ShadowClassArtifact Artifact { get; }

    public string ClassName => Artifact.ClassName;

    public string ShadowClassName => Artifact.ShadowClassName;

    public string SourceCode => Artifact.Source.SourceCode;

    public ShadowBoundary Boundary { get; }

    public IReadOnlyCollection<string> MemberNames => memberNames.AsReadOnly();

    public IReadOnlyCollection<ReferenceMapping> ReferenceMappings => Boundary.ReferenceMappings;

    public static ShadowClass Create(string className, string shadowClassName, string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowClassName);
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new ShadowClass(new ShadowClassArtifact(
            className.Trim(),
            shadowClassName.Trim(),
            new RewriteArtifactSource(sourceCode)));
    }

    public void AddMember(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (memberNames.Contains(memberName, StringComparer.Ordinal))
        {
            return;
        }

        memberNames.Add(memberName.Trim());
    }

    public void AddReferenceMapping(ReferenceMapping referenceMapping)
    {
        Boundary.AddReferenceMapping(referenceMapping);
    }
}
