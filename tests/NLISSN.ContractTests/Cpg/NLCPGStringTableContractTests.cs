using NLCPG.Builder;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class NLCPGStringTableContractTests
{
  [Fact]
  public void NodeShape_StoresStringIdsInsteadOfTextReferences()
  {
    var nodeType = typeof(NLCPGNode);
    var stringProperties = new[]
    {
      "DisplayKind",
      "Name",
      "FullName",
      "Signature",
      "TypeFullName",
      "FilePath",
    };
    var idProperties = new[]
    {
      "NameId",
      "FullNameId",
      "SignatureId",
      "TypeFullNameId",
      "FilePathId",
    };

    Assert.All(stringProperties, property => Assert.Null(nodeType.GetProperty(property)));
    Assert.All(
      idProperties,
      property => Assert.Equal(typeof(uint), nodeType.GetProperty(property)?.PropertyType));
    Assert.NotNull(typeof(NLCPGGraph).GetMethod("ResolveDisplayKind"));
    Assert.NotNull(typeof(NLCPGGraph).GetMethod("ResolveName"));
  }

  [Fact]
  public void StringInterner_UsesZeroForNullAndReusesNonEmptyTextIds()
  {
    var interner = new StringInterner();

    Assert.Equal(0u, interner.Intern(null));

    var first = interner.Intern("shared");
    var second = interner.Intern("shared");

    Assert.NotEqual(0u, first);
    Assert.Equal(first, second);
    Assert.True(interner.TryResolve(first, out var resolved));
    Assert.Equal("shared", resolved);
    Assert.False(interner.TryResolve(0, out _));
  }

  [Fact]
  public void StringInterner_ReusesIdsWhenInternedConcurrently()
  {
    var interner = new StringInterner();
    var ids = new uint[256];

    Parallel.For(
      0,
      ids.Length,
      index => ids[index] = interner.Intern("shared-concurrent"));

    Assert.NotEqual(0u, ids[0]);
    Assert.All(ids, id => Assert.Equal(ids[0], id));
    Assert.True(interner.TryResolve(ids[0], out var resolved));
    Assert.Equal("shared-concurrent", resolved);
  }

  [Fact]
  public void Graph_InternsRepeatedNodeTextOnceAndResolvesKindFromEnum()
  {
    var graph = new NLCPGBuilder().BuildFromSource(
      "namespace Demo { public sealed class Sample { public int Value(int value) => value + value; } }",
      "string-table.cs");
    var nodeType = typeof(NLCPGNode);
    var nameIdProperty = nodeType.GetProperty("NameId");
    var resolveName = typeof(NLCPGGraph).GetMethod("ResolveName");
    var resolveDisplayKind = typeof(NLCPGGraph).GetMethod("ResolveDisplayKind");

    Assert.NotNull(nameIdProperty);
    Assert.NotNull(resolveName);
    Assert.NotNull(resolveDisplayKind);

    var namedNodes = graph.Nodes
      .Select(node => new
      {
        Node = node,
        Name = resolveName!.Invoke(graph, new object[] { node }) as string,
        NameId = (uint)nameIdProperty!.GetValue(node)!,
      })
      .Where(entry => entry.Name == "value")
      .ToArray();

    Assert.True(namedNodes.Length >= 2);
    Assert.Single(namedNodes.Select(entry => entry.NameId).Distinct());
    Assert.All(
      graph.Nodes.Where(node => node.Kind is not NLCPG.Contracts.NLCPGNodeKind.SyntaxNode and
        not NLCPG.Contracts.NLCPGNodeKind.SyntaxToken),
      node => Assert.Equal(
        node.Kind.ToString(),
        resolveDisplayKind!.Invoke(graph, new object[] { node })));
  }
}
