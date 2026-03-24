namespace TerrariaTools.Dome.Core.Cpg.Tests.Schema;

public sealed class BuiltinSchemaTests
{
    [Fact]
    public void BuiltinSchema_ShouldExposeDefaultOverlayLayersAndCoreNodes()
    {
        CpgSchema schema = BuiltinSchema.Create();

        string[] layerNames = schema.Layers.Select(layer => layer.Name).ToArray();
        string[] nodeLabels = schema.Nodes.Select(node => node.Label).ToArray();

        Assert.Contains("base", layerNames);
        Assert.Contains("controlflow", layerNames);
        Assert.Contains("typerel", layerNames);
        Assert.Contains("callgraph", layerNames);

        Assert.Contains("META_DATA", nodeLabels);
        Assert.Contains("METHOD", nodeLabels);
        Assert.Contains("TYPE_DECL", nodeLabels);
        Assert.Contains("CALL", nodeLabels);
    }

    [Fact]
    public void BuiltinSchema_ShouldExposeCoreEdgesAndProperties()
    {
        CpgSchema schema = BuiltinSchema.Create();

        string[] edgeLabels = schema.Edges.Select(edge => edge.Label).ToArray();
        string[] propertyNames = schema.Properties.Select(property => property.Name).ToArray();

        Assert.Contains("AST", edgeLabels);
        Assert.Contains("CFG", edgeLabels);
        Assert.Contains("REF", edgeLabels);
        Assert.Contains("CALL", edgeLabels);

        Assert.Contains("FULL_NAME", propertyNames);
        Assert.Contains("METHOD_FULL_NAME", propertyNames);
        Assert.Contains("TYPE_FULL_NAME", propertyNames);
        Assert.Contains("CONTROL_STRUCTURE_TYPE", propertyNames);
        Assert.Contains("METHOD_NAME", propertyNames);
    }

    [Fact]
    public void SchemaIndex_ShouldExposeNodePropertiesAndEdgeMetadataFromBuiltinSchema()
    {
        Assert.Contains(PropertyKinds.FullName, SchemaIndex.NodeProperties[NodeKinds.Namespace]);
        Assert.Contains(PropertyKinds.TypeFullName, SchemaIndex.NodeProperties[NodeKinds.Method]);
        Assert.Equal("AstEdge", SchemaIndex.EdgeClrNames[EdgeKinds.Ast]);
        Assert.Equal("CallEdge", SchemaIndex.EdgeClrNames[EdgeKinds.Call]);
    }

    [Fact]
    public void SchemaIndex_ShouldExposeLayersAndPropertyMetadataFromBuiltinSchema()
    {
        Assert.Equal("base", SchemaIndex.NodeLayers[NodeKinds.Call]);
        Assert.Equal("typerel", SchemaIndex.EdgeLayers[EdgeKinds.AliasOf]);
        Assert.Equal("string", SchemaIndex.PropertyValueKinds[PropertyKinds.FullName]);
        Assert.Equal("int", SchemaIndex.PropertyValueKinds[PropertyKinds.Order]);
        Assert.False(SchemaIndex.PropertyIsRequired[PropertyKinds.Language]);
    }

    [Fact]
    public void BuiltinSchema_ShouldDeclareCoreEdgeLegalityMetadata()
    {
        CpgSchema schema = BuiltinSchema.Create();

        CpgEdgeSchema astEdge = schema.Edges.Single(edge => edge.Label == EdgeKinds.Ast);
        CpgEdgeSchema argumentEdge = schema.Edges.Single(edge => edge.Label == EdgeKinds.Argument);
        CpgEdgeSchema callEdge = schema.Edges.Single(edge => edge.Label == EdgeKinds.Call);
        CpgEdgeSchema cfgEdge = schema.Edges.Single(edge => edge.Label == EdgeKinds.Cfg);

        Assert.Contains(NodeKinds.Method, astEdge.SourceKinds);
        Assert.Contains(NodeKinds.Block, astEdge.SourceKinds);
        Assert.Contains(NodeKinds.Call, astEdge.TargetKinds);
        Assert.Contains(NodeKinds.Local, astEdge.TargetKinds);
        Assert.Contains(NodeKinds.Call, argumentEdge.SourceKinds);
        Assert.Contains(NodeKinds.Identifier, argumentEdge.TargetKinds);
        Assert.Contains(NodeKinds.Literal, argumentEdge.TargetKinds);
        Assert.Contains(NodeKinds.Method, callEdge.SourceKinds);
        Assert.Contains(NodeKinds.Method, callEdge.TargetKinds);
        Assert.Contains(NodeKinds.Method, cfgEdge.SourceKinds);
        Assert.Contains(NodeKinds.Call, cfgEdge.SourceKinds);
        Assert.Contains(NodeKinds.Return, cfgEdge.TargetKinds);
    }

    [Fact]
    public void SchemaIndex_ShouldStayAlignedWithBuiltinSchemaDefinitions()
    {
        CpgSchema schema = BuiltinSchema.Create();

        foreach (CpgNodeSchema node in schema.Nodes)
        {
            string nodeKind = CpgCodeGenerator.ToConstantName(node.Label);
            string kindValue = typeof(NodeKinds).GetField(nodeKind)!.GetRawConstantValue()!.ToString()!;

            Assert.True(SchemaIndex.NodeClrNames.ContainsKey(kindValue));
            Assert.True(SchemaIndex.NodeProperties.ContainsKey(kindValue));
            Assert.Equal(node.Properties, SchemaIndex.NodeProperties[kindValue]);
        }

        foreach (CpgEdgeSchema edge in schema.Edges)
        {
            string edgeKind = CpgCodeGenerator.ToConstantName(edge.Label);
            string kindValue = typeof(EdgeKinds).GetField(edgeKind)!.GetRawConstantValue()!.ToString()!;

            Assert.True(SchemaIndex.EdgeClrNames.ContainsKey(kindValue));
            Assert.Equal(edge.ClrName, SchemaIndex.EdgeClrNames[kindValue]);
            Assert.Equal(edge.SourceKinds, SchemaIndex.EdgeSourceKinds[kindValue]);
            Assert.Equal(edge.TargetKinds, SchemaIndex.EdgeTargetKinds[kindValue]);
        }
    }

    [Fact]
    public void GeneratedNodePublicProperties_ShouldMatchSchemaIndexDefinitions()
    {
        foreach ((string nodeKind, string clrName) in SchemaIndex.NodeClrNames)
        {
            Type nodeType = typeof(StoredNode).Assembly
                .GetType($"{typeof(StoredNode).Namespace}.{clrName}", throwOnError: true)!;

            string[] actualProperties = nodeType
                .GetProperties()
                .Where(property => property.DeclaringType == nodeType)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            string[] expectedProperties = SchemaIndex.NodeProperties[nodeKind]
                .Select(CpgCodeGenerator.ToConstantName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedProperties, actualProperties);
        }
    }
}
