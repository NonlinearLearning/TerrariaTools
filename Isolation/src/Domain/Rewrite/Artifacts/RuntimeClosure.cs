using Domain.Propagation;

namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示最小运行闭包结果。
/// </summary>
public sealed class RuntimeClosure
{
    private readonly List<string> memberNames = new();

    private RuntimeClosure(RuntimeClosureArtifact artifact)
    {
        Artifact = artifact;
        Boundary = RuntimeClosureBoundary.Create(new ClosureRoot(artifact.ClassName, artifact.RootMethodName));
    }

    public RuntimeClosureArtifact Artifact { get; }

    public string ClassName => Artifact.ClassName;

    public string RootMethodName => Artifact.RootMethodName;

    public string ClosureClassName => Artifact.ClosureClassName;

    public string SourceCode => Artifact.Source.SourceCode;

    public RuntimeClosureBoundary Boundary { get; }

    public ClosureRoot Root => Boundary.Root;

    public ClosureIntegrityStatus IntegrityStatus => Boundary.IntegrityStatus;

    public IReadOnlyCollection<string> MemberNames => memberNames.AsReadOnly();

    public IReadOnlyCollection<ReferenceMapping> ReferenceMappings => Boundary.ReferenceMappings;

    public static RuntimeClosure Create(
        string className,
        string rootMethodName,
        string closureClassName,
        string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootMethodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(closureClassName);
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new RuntimeClosure(new RuntimeClosureArtifact(
            className.Trim(),
            rootMethodName.Trim(),
            closureClassName.Trim(),
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

    public void MarkIntegrity(ClosureIntegrityStatus integrityStatus)
    {
        Boundary.MarkIntegrity(integrityStatus);
    }
}
