using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using Xunit;

namespace Isolation.AnalysisTests.Naming;

public sealed class NamingGovernanceConventionsTests
{
    private static readonly string[] ApprovedLegacyLowercaseDirectories =
    [
        "src/Logic/Analysis/Engine/Language/android",
        "src/Logic/Analysis/Engine/Language/bindingextension",
        "src/Logic/Analysis/Engine/Language/callgraphextension",
        "src/Logic/Analysis/Engine/Language/DataFlow/dotextension",
        "src/Logic/Analysis/Engine/Language/DataFlow/nodemethods",
        "src/Logic/Analysis/Engine/Language/dotextension",
        "src/Logic/Analysis/Engine/Language/importresolver",
        "src/Logic/Analysis/Engine/Language/modulevariable",
        "src/Logic/Analysis/Engine/Language/modulevariable/nodemethods",
        "src/Logic/Analysis/Engine/Language/nodemethods",
        "src/Logic/Analysis/Engine/Language/operatorextension",
        "src/Logic/Analysis/Engine/Language/operatorextension/nodemethods",
        "src/Logic/Analysis/Engine/Language/types",
        "src/Logic/Analysis/Engine/Language/types/expressions",
        "src/Logic/Analysis/Engine/Language/types/expressions/generalizations",
        "src/Logic/Analysis/Engine/Language/types/propertyaccessors",
        "src/Logic/Analysis/Engine/Language/types/structure",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> ApprovedContractsMultiTypeHotspots =
        new Dictionary<string, string[]>();

    private static readonly IReadOnlyDictionary<string, string[]> ApprovedDomainAggregateMultiTypeFiles =
        new Dictionary<string, string[]>
        {
            ["src/Domain/Analysis/Engine/Semantic/AccessPath/AccessElement.cs"] =
            ["AccessElement", "ConstantAccess", "VariableAccess", "VariablePointerShift", "IndirectionAccess", "AddressOf", "PointerShift"],
            ["src/Domain/Analysis/Engine/Semantic/AccessPath/TrackedBase.cs"] =
            ["ITrackedBase", "TrackedNamedVariable", "TrackedReturnValue", "TrackedLiteral", "TrackedMethod", "TrackedTypeRef", "TrackedAlias", "TrackedUnknown", "TrackedFormalReturn"],
            ["src/Domain/Analysis/Engine/Semantic/Flows/FlowEndpoint.cs"] =
            ["FlowEndpoint", "FlowEndpointKind"],
            ["src/Domain/Analysis/Engine/Semantic/Flows/Semantics.cs"] =
            ["ISemantics", "NoSemantics", "CompositeSemantics"],
            ["src/Domain/Propagation/ChangeCandidate.cs"] =
            ["ChangeCandidate", "CandidateKind", "ScenarioTag"],
            ["src/Domain/Rules/RuleOutcome.cs"] =
            ["RuleOutcome", "RuleOutcomeStatus", "RuleOutcomeSeverity"],
            ["src/Domain/Rules/RuleReason.cs"] =
            ["RuleReason", "RuleReasonKind", "RuleReasonRiskLevel"],
            ["src/Domain/Workspaces/InputDescriptor.cs"] =
            ["InputDescriptor", "InputOrigin"],
        };

    private static readonly IReadOnlyDictionary<string, string[]> ApprovedDomainTransitionalMultiTypeFiles =
        new Dictionary<string, string[]>
        {
        };

    private static readonly IReadOnlyDictionary<string, string> ApprovedQueryCanonicalCoreTypes =
        new Dictionary<string, string>
        {
            ["src/Domain/Analysis/Engine/Query/QueryEngine.cs"] = "QueryEngine",
            ["src/Domain/Analysis/Engine/Query/QueryTaskCreator.cs"] = "QueryTaskCreator",
            ["src/Domain/Analysis/Engine/Query/QueryTaskSolver.cs"] = "QueryTaskSolver",
        };

    private static readonly string[] ApprovedControlDependenceGraphFiles =
    [
        "src/Logic/Analysis/Engine/Passes/ControlFlow/BuildCdgPass.cs",
        "src/Logic/Analysis/Engine/Passes/ControlFlow/ControlDependenceGraph/CpgPostDomTreeAdapter.cs",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> ApprovedGenericTechnicalSuffixHotspots =
        new Dictionary<string, string[]>
        {
            ["src/Domain/Common/Events/DomainEventBase.cs"] = ["DomainEventBase"],
            ["src/Domain/Analysis/Engine/Semantic/AccessPath/TrackedBase.cs"] = ["ITrackedBase"],
            ["src/Logic/Analysis/Engine/Frontend/CpgFrontendBase.cs"] = ["CpgFrontendBase"],
            ["src/Logic/Analysis/Engine/Frontend/AstModel/AstCreatorBase.cs"] = ["AstCreatorBase"],
            ["src/Logic/Analysis/Engine/Layers/LayerCreatorBase.cs"] = ["LayerCreatorBase"],
            ["src/Logic/Analysis/Engine/Frontend/FrontendGraphConventions.cs"] = ["InvocationReceiverInfo"],
            ["src/Logic/Analysis/Engine/Passes/ImportDirectiveInfo.cs"] = ["ImportDirectiveInfo"],
            ["src/Logic/Analysis/Engine/Passes/ControlFlow/CfgModel.cs"] = ["CfgModel"],
            ["src/Logic/Analysis/Engine/X2Cpg/DataStructures/VariableScopeManager.cs"] = ["VariableScopeManager"],
            ["src/Logic/Analysis/Engine/X2Cpg/TypeStubs/TypeStubConfig.cs"] = ["TypeStubMetaData"],
        };

    [Fact]
    public void Legacy_lowercase_source_directories_stay_on_allowlist()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src");

        string[] unexpectedDirectories = Directory
            .EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactDirectory(path))
            .Select(path => NormalizeRelativePath(repositoryRoot, path))
            .Where(path => !IsPascalCase(Path.GetFileName(path)))
            .Where(path => !ApprovedLegacyLowercaseDirectories.Contains(path, StringComparer.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedDirectories);
    }

    [Fact]
    public void Application_contract_multi_type_files_stay_within_known_hotspots()
    {
        string repositoryRoot = FindRepositoryRoot();
        string contractsRoot = Path.Combine(repositoryRoot, "src", "Application", "Contracts");

        var unexpectedHotspots = Directory
            .EnumerateFiles(contractsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CreateFileShape(repositoryRoot, path))
            .Where(shape => shape.TypeNames.Count > 1)
            .Where(shape => !ApprovedContractsMultiTypeHotspots.ContainsKey(shape.RelativePath))
            .Select(shape => $"{shape.RelativePath} => {string.Join(", ", shape.TypeNames)}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedHotspots);

        foreach (FileShape hotspot in Directory
                     .EnumerateFiles(contractsRoot, "*.cs", SearchOption.AllDirectories)
                     .Select(path => CreateFileShape(repositoryRoot, path))
                     .Where(shape => shape.TypeNames.Count > 1))
        {
            string[] approvedTypes = ApprovedContractsMultiTypeHotspots[hotspot.RelativePath];
            string[] unexpectedTypes = hotspot.TypeNames
                .Where(typeName => !approvedTypes.Contains(typeName, StringComparer.Ordinal))
                .ToArray();

            Assert.Empty(unexpectedTypes);
            Assert.True(
                hotspot.TypeNames.Count <= approvedTypes.Length,
                $"{hotspot.RelativePath} 的多主类型聚合范围扩大了：{string.Join(", ", hotspot.TypeNames)}");
        }
    }

    [Fact]
    public void Application_contract_single_type_files_match_file_name()
    {
        string repositoryRoot = FindRepositoryRoot();
        string contractsRoot = Path.Combine(repositoryRoot, "src", "Application", "Contracts");

        string[] mismatches = Directory
            .EnumerateFiles(contractsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CreateFileShape(repositoryRoot, path))
            .Where(shape => shape.TypeNames.Count == 1)
            .Where(shape => !string.Equals(shape.FileBaseName, shape.TypeNames[0], StringComparison.Ordinal))
            .Select(shape => $"{shape.RelativePath} => {shape.TypeNames[0]}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Application_contract_public_type_names_follow_role_naming()
    {
        string repositoryRoot = FindRepositoryRoot();
        string contractsRoot = Path.Combine(repositoryRoot, "src", "Application", "Contracts");

        string[] unexpectedTypes = Directory
            .EnumerateFiles(contractsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CreateFileShape(repositoryRoot, path))
            .SelectMany(shape => shape.TypeNames.Select(typeName => $"{shape.RelativePath} => {typeName}"))
            .Where(item =>
            {
                string typeName = item.Split(" => ", StringSplitOptions.None)[1];
                return !IsApprovedContractTypeName(typeName);
            })
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedTypes);
    }

    [Fact]
    public void Domain_multi_type_files_stay_within_governed_allowlists()
    {
        string repositoryRoot = FindRepositoryRoot();
        string domainRoot = Path.Combine(repositoryRoot, "src", "Domain");
        IReadOnlyDictionary<string, string[]> approvedHotspots = ApprovedDomainAggregateMultiTypeFiles
            .Concat(ApprovedDomainTransitionalMultiTypeFiles)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var unexpectedHotspots = Directory
            .EnumerateFiles(domainRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CreateFileShape(repositoryRoot, path))
            .Where(shape => shape.TypeNames.Count > 1)
            .Where(shape => !approvedHotspots.ContainsKey(shape.RelativePath))
            .Select(shape => $"{shape.RelativePath} => {string.Join(", ", shape.TypeNames)}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedHotspots);

        foreach (FileShape hotspot in Directory
                     .EnumerateFiles(domainRoot, "*.cs", SearchOption.AllDirectories)
                     .Select(path => CreateFileShape(repositoryRoot, path))
                     .Where(shape => shape.TypeNames.Count > 1))
        {
            string[] approvedTypes = approvedHotspots[hotspot.RelativePath];
            string[] unexpectedTypes = hotspot.TypeNames
                .Where(typeName => !approvedTypes.Contains(typeName, StringComparer.Ordinal))
                .ToArray();

            Assert.Empty(unexpectedTypes);
            Assert.True(
                hotspot.TypeNames.Count <= approvedTypes.Length,
                $"{hotspot.RelativePath} 的多主类型聚合范围扩大了：{string.Join(", ", hotspot.TypeNames)}");
        }
    }

    [Fact]
    public void Query_role_types_stay_confined_to_canonical_core_and_compat_facade_sets()
    {
        string repositoryRoot = FindRepositoryRoot();
        string queryRoot = Path.Combine(repositoryRoot, "src", "Domain", "Analysis", "Engine", "Query");
        IReadOnlyDictionary<string, string> approvedRoleTypes = ApprovedQueryCanonicalCoreTypes;

        FileShape[] roleTypeFiles = Directory
            .EnumerateFiles(queryRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CreateFileShape(repositoryRoot, path))
            .Where(shape => shape.TypeNames.Any(IsQueryRoleTypeName))
            .OrderBy(shape => shape.RelativePath, StringComparer.Ordinal)
            .ToArray();

        string[] unexpectedRoleFiles = roleTypeFiles
            .Where(shape => !approvedRoleTypes.ContainsKey(shape.RelativePath))
            .Select(shape => $"{shape.RelativePath} => {string.Join(", ", shape.TypeNames)}")
            .ToArray();

        Assert.Empty(unexpectedRoleFiles);

        foreach (FileShape shape in roleTypeFiles)
        {
            Assert.True(
                approvedRoleTypes.TryGetValue(shape.RelativePath, out string? approvedType),
                $"未登记的 Query 角色文件：{shape.RelativePath}");

            string[] matchingRoleTypes = shape.TypeNames.Where(IsQueryRoleTypeName).ToArray();
            Assert.Equal([approvedType], matchingRoleTypes);
            Assert.Equal(shape.FileBaseName, approvedType);
        }

        Assert.Equal(
            ApprovedQueryCanonicalCoreTypes.Values.OrderBy(value => value, StringComparer.Ordinal),
            roleTypeFiles
                .SelectMany(shape => shape.TypeNames)
                .Where(typeName => ApprovedQueryCanonicalCoreTypes.Values.Contains(typeName, StringComparer.Ordinal))
                .OrderBy(value => value, StringComparer.Ordinal));

    }

    [Fact]
    public void Control_dependence_graph_naming_stays_confined_to_known_hotspots()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src");

        string[] unexpectedFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactFile(path))
            .Where(path => File.ReadAllText(path).Contains("ControlDependenceGraph", StringComparison.Ordinal))
            .Select(path => NormalizeRelativePath(repositoryRoot, path))
            .Where(path => !ApprovedControlDependenceGraphFiles.Contains(path, StringComparer.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedFiles);
    }

    [Fact]
    public void Public_non_interface_types_do_not_use_interface_prefixes()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src");

        string[] offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactFile(path))
            .SelectMany(path => CreatePublicTypeShapes(repositoryRoot, path))
            .Where(shape => !shape.IsInterface)
            .Where(shape => shape.TypeName.Length > 1 && shape.TypeName[0] == 'I' && char.IsUpper(shape.TypeName[1]))
            .Select(shape => $"{shape.RelativePath} => {shape.TypeName}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Public_static_extension_containers_use_plural_extension_suffixes()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src");

        string[] offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactFile(path))
            .SelectMany(path => CreatePublicTypeShapes(repositoryRoot, path))
            .Where(shape => shape.IsStatic)
            .Where(shape => shape.TypeName.EndsWith("Extension", StringComparison.Ordinal))
            .Select(shape => $"{shape.RelativePath} => {shape.TypeName}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static FileShape CreateFileShape(string repositoryRoot, string filePath)
    {
        string relativePath = NormalizeRelativePath(repositoryRoot, filePath);
        string fileBaseName = Path.GetFileNameWithoutExtension(filePath);
        string[] typeNames = GetPublicTopLevelTypeNames(filePath).ToArray();
        return new FileShape(relativePath, fileBaseName, typeNames);
    }

    private static IReadOnlyList<PublicTypeShape> CreatePublicTypeShapes(string repositoryRoot, string filePath)
    {
        string relativePath = NormalizeRelativePath(repositoryRoot, filePath);
        return GetPublicTopLevelTypeShapes(filePath)
            .Select(shape => shape with { RelativePath = relativePath })
            .ToArray();
    }

    private static bool IsQueryRoleTypeName(string typeName)
    {
        return typeName.EndsWith("Engine", StringComparison.Ordinal) ||
               typeName.EndsWith("Creator", StringComparison.Ordinal) ||
               typeName.EndsWith("Solver", StringComparison.Ordinal);
    }

    private static bool IsGenericTechnicalSuffixTypeName(string typeName)
    {
        return typeName.EndsWith("Base", StringComparison.Ordinal) ||
               typeName.EndsWith("Manager", StringComparison.Ordinal) ||
               typeName.EndsWith("Info", StringComparison.Ordinal) ||
               typeName.EndsWith("Model", StringComparison.Ordinal) ||
               typeName.EndsWith("MetaData", StringComparison.Ordinal);
    }

    private static bool IsApprovedContractTypeName(string typeName)
    {
        return typeName.StartsWith("Contract", StringComparison.Ordinal) ||
               typeName.EndsWith("Dto", StringComparison.Ordinal) ||
               typeName.EndsWith("Request", StringComparison.Ordinal) ||
               typeName.EndsWith("Response", StringComparison.Ordinal) ||
               typeName.EndsWith("Command", StringComparison.Ordinal) ||
               typeName.EndsWith("Query", StringComparison.Ordinal) ||
               typeName.EndsWith("AppService", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetPublicTopLevelTypeNames(string filePath)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(File.ReadAllText(filePath), path: filePath)
            .GetCompilationUnitRoot();
        List<string> names = [];
        CollectPublicTopLevelTypeNames(root.Members, names);
        return names;
    }

    private static IReadOnlyList<PublicTypeShape> GetPublicTopLevelTypeShapes(string filePath)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(File.ReadAllText(filePath), path: filePath)
            .GetCompilationUnitRoot();
        List<PublicTypeShape> shapes = [];
        CollectPublicTopLevelTypeShapes(root.Members, shapes);
        return shapes;
    }

    private static void CollectPublicTopLevelTypeNames(
        SyntaxList<MemberDeclarationSyntax> members,
        List<string> names)
    {
        foreach (MemberDeclarationSyntax member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    CollectPublicTopLevelTypeNames(namespaceDeclaration.Members, names);
                    break;
                case BaseTypeDeclarationSyntax typeDeclaration
                    when typeDeclaration.Modifiers.Any(
                        modifier => modifier.IsKind(SyntaxKind.PublicKeyword)):
                    names.Add(typeDeclaration.Identifier.Text);
                    break;
            }
        }
    }

    private static void CollectPublicTopLevelTypeShapes(
        SyntaxList<MemberDeclarationSyntax> members,
        List<PublicTypeShape> shapes)
    {
        foreach (MemberDeclarationSyntax member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    CollectPublicTopLevelTypeShapes(namespaceDeclaration.Members, shapes);
                    break;
                case BaseTypeDeclarationSyntax typeDeclaration
                    when typeDeclaration.Modifiers.Any(
                        modifier => modifier.IsKind(SyntaxKind.PublicKeyword)):
                    shapes.Add(
                        new PublicTypeShape(
                            RelativePath: string.Empty,
                            TypeName: typeDeclaration.Identifier.Text,
                            IsInterface: typeDeclaration is InterfaceDeclarationSyntax,
                            IsStatic: typeDeclaration.Modifiers.Any(
                                modifier => modifier.IsKind(SyntaxKind.StaticKeyword))));
                    break;
            }
        }
    }

    private static bool IsBuildArtifactDirectory(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
               path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsBuildArtifactFile(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
               path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsPascalCase(string name)
    {
        return name.Length > 0 &&
               char.IsUpper(name[0]) &&
               name.All(char.IsLetterOrDigit);
    }

    private static string NormalizeRelativePath(string repositoryRoot, string path)
    {
        return Path
            .GetRelativePath(repositoryRoot, path)
            .Replace('\\', '/');
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

    private sealed record FileShape(
        string RelativePath,
        string FileBaseName,
        IReadOnlyList<string> TypeNames);

    private sealed record PublicTypeShape(
        string RelativePath,
        string TypeName,
        bool IsInterface,
        bool IsStatic);
}

