using System.Runtime.CompilerServices;
using Xunit;

namespace Isolation.AnalysisTests.Documentation;

public sealed class DocsAsCodeAlignmentTests
{
    private static readonly string[] ForbiddenHardDependencyPhrases =
    [
        "显式加载 `ai-rules/common/prompt-spec-writing.mdc`",
        "默认同步检查 `ai-rules/`、`.codex/skills/`、`.agents/skills/` 是否仍与本文口径一致。",
    ];

    [Fact]
    public void Core_architecture_docs_do_not_require_absent_authoring_sidecars()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] documents =
        [
            "docs/架构设计.md",
            "docs/DDD/src目录分层说明.md",
        ];

        foreach (string relativePath in documents)
        {
            string content = ReadRepositoryFile(repositoryRoot, relativePath);

            foreach (string forbiddenHint in ForbiddenHardDependencyPhrases)
            {
                Assert.DoesNotContain(
                    forbiddenHint,
                    content,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Ddd_docs_as_code_evidence_plan_stays_bound_to_authoritative_sources_and_live_anchors()
    {
        string repositoryRoot = FindRepositoryRoot();
        const string RelativePlanPath = "docs/plans/2026-04-21-worker3-ddd-docs-as-code-evidence-plan.md";
        string planContent = ReadRepositoryFile(repositoryRoot, RelativePlanPath);

        string[] expectedSources =
        [
            "https://martinfowler.com/eaaCatalog/serviceLayer.html",
            "https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design",
            "https://www.writethedocs.org/guide/docs-as-code/",
            "https://docs.gitlab.com/development/documentation/workflow/",
            "https://google.aip.dev/192",
            "https://www.thoughtworks.com/en-us/radar/techniques/architectural-fitness-function",
        ];

        foreach (string source in expectedSources)
        {
            Assert.Contains(source, planContent, StringComparison.Ordinal);
        }

        string[] requiredAnchors =
        [
            "src/Application/Services/RewriteWorkflowAppService.cs",
            "src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs",
            "src/Logic/Rules/RewriteWorkflowRulePreset.cs",
            "tests/ArchitectureTests/Program.cs",
            "tests/Isolation.AnalysisTests/Documentation/DocsAsCodeAlignmentTests.cs",
        ];

        foreach (string anchor in requiredAnchors)
        {
            Assert.Contains(anchor, planContent, StringComparison.Ordinal);
            Assert.True(
                File.Exists(Path.Combine(repositoryRoot, anchor.Replace('/', Path.DirectorySeparatorChar))),
                $"缺失锚点文件：{anchor}");
        }
    }

    [Fact]
    public void Core_architecture_docs_stay_linked_to_current_repo_test_anchors()
    {
        string repositoryRoot = FindRepositoryRoot();

        AssertDocumentContainsAnchor(
            repositoryRoot,
            "docs/架构设计.md",
            "tests/Isolation.AnalysisTests/Documentation/DocsAsCodeAlignmentTests.cs");
        AssertDocumentContainsAnchor(
            repositoryRoot,
            "docs/DDD/src目录分层说明.md",
            "tests/Isolation.AnalysisTests/Documentation/DocsAsCodeAlignmentTests.cs");
        AssertDocumentContainsAnchor(
            repositoryRoot,
            "docs/架构设计.md",
            "docs/plans/2026-04-21-worker3-ddd-docs-as-code-evidence-plan.md");
        AssertDocumentContainsAnchor(
            repositoryRoot,
            "docs/DDD/src目录分层说明.md",
            "docs/plans/2026-04-21-worker3-ddd-docs-as-code-evidence-plan.md");
    }

    private static void AssertDocumentContainsAnchor(string repositoryRoot, string relativePath, string expectedText)
    {
        string content = ReadRepositoryFile(repositoryRoot, relativePath);
        Assert.Contains(expectedText, content, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string repositoryRoot, string relativePath)
    {
        string absolutePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absolutePath), $"缺失文档：{relativePath}");
        return File.ReadAllText(absolutePath);
    }

    private static string FindRepositoryRoot()
    {
        string filePath = GetCurrentSourceFilePath();
        string directory = Path.GetDirectoryName(filePath)!;
        return Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
    }

    private static string GetCurrentSourceFilePath([CallerFilePath] string filePath = "")
    {
        return filePath;
    }
}
