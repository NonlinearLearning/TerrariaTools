namespace TerrariaTools.Dome.Core.Cpg.Tests.Schema;

public sealed class CodeGenerationTests
{
    [Fact]
    public void CpgCodeGenerator_ShouldIncludeMethodNodeInGeneratedSource()
    {
        CpgCodeGenerator generator = new();

        string source = generator.BuildNodeTypesSource(BuiltinSchema.Create());

        Assert.Contains("public sealed class MethodNode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedTypes_ShouldExposeMethodNodeAsAstAndCfgDeclaration()
    {
        MethodNode node = new("method-1");

        Assert.IsAssignableFrom<AstNode>(node);
        Assert.IsAssignableFrom<ICfgNode>(node);
        Assert.IsAssignableFrom<IDeclarationNode>(node);
    }

    [Fact]
    public void GeneratedConstants_ShouldExposeCoreEdgeKinds()
    {
        Assert.Equal("AST", EdgeKinds.Ast);
        Assert.Equal("REF", EdgeKinds.Ref);
        Assert.Equal("CFG", EdgeKinds.Cfg);
        Assert.Equal("INHERITS_FROM", EdgeKinds.InheritsFrom);
    }

    [Fact]
    public void GeneratedNodes_ShouldExposeSchemaAlignedPropertiesForNamespaceFileAndMethod()
    {
        NamespaceNode namespaceNode = new("namespace:N", "N", "N");
        NamespaceBlockNode namespaceBlockNode = new("namespace-block:N", "N", "N");
        FileNode fileNode = new("file:input.cs", "input.cs");
        MethodNode methodNode = new("method:N.C.M", "M", "N.C", "int", "N.C.M", "int()", "int");

        Assert.Equal("N", namespaceNode.FullName);
        Assert.Equal("N", namespaceBlockNode.FullName);
        Assert.Equal("input.cs", fileNode.Name);
        Assert.Equal("int", methodNode.TypeFullName);
    }

    [Fact]
    public void GeneratedNodes_ShouldExposeSchemaAlignedPropertiesForMetaData()
    {
        MetaDataNode metaDataNode = new("meta-data", "CSHARP", "input.cs", "0.1");

        Assert.Equal("CSHARP", metaDataNode.Language);
        Assert.Equal("input.cs", metaDataNode.Root);
        Assert.Equal("0.1", metaDataNode.Version);
    }

    [Fact]
    public void CpgCodeGenerator_ShouldBuildNodeKindsInterfacesAndBaseTypesSources()
    {
        CpgCodeGenerator generator = new();
        CpgSchema schema = BuiltinSchema.Create();

        string nodeKindsSource = generator.BuildNodeKindsSource(schema);
        string nodeInterfacesSource = generator.BuildNodeInterfacesSource(schema);
        string nodeBaseTypesSource = generator.BuildNodeBaseTypesSource(schema);

        Assert.Contains("public const string MetaData = \"META_DATA\";", nodeKindsSource, StringComparison.Ordinal);
        Assert.Contains("public interface ICfgNode", nodeInterfacesSource, StringComparison.Ordinal);
        Assert.Contains(
            "public abstract class ExpressionNode(string id) : AstNode(id), ICfgNode",
            nodeBaseTypesSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CpgCodeGenerator_ShouldBuildEdgePropertyAndSchemaIndexSources()
    {
        CpgCodeGenerator generator = new();
        CpgSchema schema = BuiltinSchema.Create();

        string edgeKindsSource = generator.BuildEdgeKindsSource(schema);
        string propertyKindsSource = generator.BuildPropertyKindsSource(schema);
        string schemaIndexSource = generator.BuildSchemaIndexSource(schema);

        Assert.Contains("public const string Ast = \"AST\";", edgeKindsSource, StringComparison.Ordinal);
        Assert.Contains("public const string MethodFullName = \"METHOD_FULL_NAME\";", propertyKindsSource, StringComparison.Ordinal);
        Assert.Contains("[NodeKinds.MetaData] = nameof(MetaDataNode)", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains(
            "[NodeKinds.Method] = [PropertyKinds.Name, PropertyKinds.ContainingTypeName, PropertyKinds.ReturnTypeName, PropertyKinds.FullName, PropertyKinds.Signature, PropertyKinds.TypeFullName]",
            schemaIndexSource,
            StringComparison.Ordinal);
        Assert.Contains("[EdgeKinds.Call] = \"CallEdge\"", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[NodeKinds.Call] = \"base\"", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[EdgeKinds.AliasOf] = \"typerel\"", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EdgeSourceKinds =", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EdgeTargetKinds =", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[EdgeKinds.Call] = [NodeKinds.Method]", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[EdgeKinds.Argument] = [NodeKinds.Identifier, NodeKinds.Literal, NodeKinds.Call, NodeKinds.MethodRef, NodeKinds.FieldIdentifier]", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[PropertyKinds.FullName] = \"string\"", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[PropertyKinds.Order] = \"int\"", schemaIndexSource, StringComparison.Ordinal);
        Assert.Contains("[PropertyKinds.Language] = false", schemaIndexSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CpgCodeGenerator_ShouldMatchCheckedInGeneratedConstantsAndMetadata()
    {
        CpgCodeGenerator generator = new();
        CpgSchema schema = BuiltinSchema.Create();

        Assert.Equal(
            ReadGeneratedFile("NodeKinds.g.cs"),
            NormalizeLineEndings(generator.BuildNodeKindsSource(schema)));
        Assert.Equal(
            ReadGeneratedFile("EdgeKinds.g.cs"),
            NormalizeLineEndings(generator.BuildEdgeKindsSource(schema)));
        Assert.Equal(
            ReadGeneratedFile("PropertyKinds.g.cs"),
            NormalizeLineEndings(generator.BuildPropertyKindsSource(schema)));
        Assert.Equal(
            ReadGeneratedFile("NodeInterfaces.g.cs"),
            NormalizeLineEndings(generator.BuildNodeInterfacesSource(schema)));
        Assert.Equal(
            ReadGeneratedFile("NodeBaseTypes.g.cs"),
            NormalizeLineEndings(generator.BuildNodeBaseTypesSource(schema)));
        Assert.Equal(
            ReadGeneratedFile("SchemaIndex.g.cs"),
            NormalizeLineEndings(generator.BuildSchemaIndexSource(schema)));
    }

    [Fact]
    public void CpgCodeGenerator_ShouldMatchCheckedInGeneratedNodeTypes()
    {
        CpgCodeGenerator generator = new();

        Assert.Equal(
            ReadGeneratedFile("NodeTypes.g.cs"),
            NormalizeLineEndings(generator.BuildNodeTypesSource(BuiltinSchema.Create())));
    }

    private static string ReadGeneratedFile(string fileName)
    {
        string generatedDirectory = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "dome",
                "src",
                "Core",
                "CPG",
                "Generated",
                fileName));
        return NormalizeLineEndings(File.ReadAllText(generatedDirectory));
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }
}
