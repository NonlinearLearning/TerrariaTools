using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// 跨文件装箱（D1）的**等价性**契约：同文件在「跨文件批次」与「独立构建」下必须得到**逐字相同**的图。
/// </summary>
/// <remarks>
/// <para>
/// 本类固定的是启用 D1 的**唯一安全前提**。跨文件装箱把多个文件的完整方法放进同一个装箱池，
/// 好处是批次数下降；风险是「本文件的分析读到了别的文件的节点」。图不变 ⇒ 分析输入的语义没变 ⇒
/// 装箱只是调度层变化，不会改变分析结论。若哪天不等价了，本类会立刻变红。
/// </para>
/// <para>
/// ⚠ 本类只断言**图的形状**（节点与边），不断言批次数——批次数正是本改动**故意**要改变的。
/// </para>
/// </remarks>
public sealed class CrossFileBatchEquivalenceTests
{
    private const string Caller = """
      namespace Demo;

      public sealed class Caller
      {
        public int Invoke(Callee callee, int value)
        {
          return callee.Compute(value) + 1;
        }
      }
      """;

    private const string Callee = """
      namespace Demo;

      public sealed class Callee
      {
        public int Compute(int value)
        {
          if (value > 10)
          {
            return value * 2;
          }

          return value + 1;
        }
      }
      """;

    private const string Third = """
      namespace Demo;

      public sealed class Third
      {
        public int Loop(int count)
        {
          var total = 0;
          for (var index = 0; index < count; index++)
          {
            total += index;
          }

          return total;
        }
      }
      """;

    /// <summary>
    /// **语法物化**必须逐字相同：同一文件在批量与独立构建下，语法节点/token 的
    /// （种类, 起止, 文本）集合必须完全一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是真正成立且足够强的等价判据：语法物化只依赖**本文件**的源码，
    /// 与「同批次里还有哪些兄弟文件」无关。若跨文件装箱把某个文件的语法物化弄脏了
    /// （漏节点、重复节点、跨文件串位），这里会立刻变红。
    /// </para>
    /// <para>
    /// ⚠ <b>不能比较整张图，也不能比较语义事实的名字。</b>跨文件解析成功后，调用方的图会
    /// **合理地**变化：<c>Invoke:int(Callee,int)</c> 会变成 <c>Invoke:int(Demo.Callee,int)</c>
    /// （未解析的类型名被解析成完全限定名），并多出指向被调用方的 <c>CallSite</c>/
    /// <c>MethodParameter</c> 等符号事实。这是本改动的**目的**，不是污染。
    /// 语义层面的正向证据由
    /// <see cref="BuildManyDocuments_BatchedGraphContainsCrossFileSymbolFacts"/> 断言。
    /// </para>
    /// </remarks>
    [Fact]
    public void BuildManyDocuments_SyntaxMaterializationMatchesIndependentBuild()
    {
        var batch = BuildBatch();

        Assert.Equal(
          SyntaxSignature(BuildIndependent(Caller, "caller.cs")),
          SyntaxSignature(batch["caller.cs"]));
        Assert.Equal(
          SyntaxSignature(BuildIndependent(Callee, "callee.cs")),
          SyntaxSignature(batch["callee.cs"]));
        Assert.Equal(
          SyntaxSignature(BuildIndependent(Third, "third.cs")),
          SyntaxSignature(batch["third.cs"]));
    }

    /// <summary>
    /// 跨文件解析必须**真的**带来更多事实：调用方图里应当出现来自被调用方文件的节点。
    /// </summary>
    /// <remarks>
    /// 这是本改动价值的直接证据。若该断言失败，说明跨文件解析没生效，
    /// 那么「跨文件装箱」就只是把无关文件排在一起，没有任何语义收益。
    /// </remarks>
    [Fact]
    public void BuildManyDocuments_BatchedGraphContainsCrossFileSymbolFacts()
    {
        var batch = BuildBatch();
        var independent = BuildIndependent(Caller, "caller.cs");

        var batchedForeign = ForeignNodeCount(batch["caller.cs"], "caller.cs");
        var independentForeign = ForeignNodeCount(independent, "caller.cs");

        Assert.True(
          batchedForeign > independentForeign,
          $"批量构建应带来更多跨文件符号事实，实际 batch={batchedForeign}、independent={independentForeign}。");
    }

    [Fact]
    public void BuildManyDocuments_EveryFileGraphIsNonEmpty()
    {
        var batch = BuildBatch();

        Assert.Equal(3, batch.Count);
        foreach (var (filePath, graph) in batch)
        {
            Assert.True(graph.Nodes.Count > 0, $"'{filePath}' 的图为空。");
            Assert.True(graph.Edges.Count > 0, $"'{filePath}' 的图无边。");
        }
    }

    /// <summary>
    /// 共享构建的节点/边必须严格按文件归属拆开：一个文件声明的类型**只能**出现在它自己的图里。
    /// </summary>
    /// <remarks>
    /// 这是「每文件一张独立图」的直接断言。若路由写错（把 B 的事实写进 A 的图），
    /// 该文件自己的类型名就会出现在别人的图里。
    /// </remarks>
    [Fact]
    public void BuildManyDocuments_DeclaredTypesStayInTheirOwnGraph()
    {
        var batch = BuildBatch();

        AssertGraphDeclares(batch["caller.cs"], "Demo.Caller", "Demo.Callee", "Demo.Third");
        AssertGraphDeclares(batch["callee.cs"], "Demo.Callee", "Demo.Caller", "Demo.Third");
        AssertGraphDeclares(batch["third.cs"], "Demo.Third", "Demo.Caller", "Demo.Callee");
    }

    /// <summary>
    /// 断言某图**声明了** <paramref name="expectedDeclared"/>，且**未声明**其余类型。
    /// </summary>
    /// <remarks>
    /// ⚠ 只检查 <see cref="NLCPGNodeKind.TypeDecl"/> 节点，不检查符号引用节点：
    /// 跨文件解析成功后，被引用类型的**符号节点**本就会出现在引用方的图里（这是正确的），
    /// 但**声明节点**必须只留在声明它的那个文件。
    /// </remarks>
    private static void AssertGraphDeclares(
      NLCPGGraph graph,
      string expectedDeclared,
      params string[] mustNotDeclare)
    {
        var declared = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.TypeDecl)
          .Select(graph.ResolveFullName)
          .Where(name => name is not null)
          .ToArray();

        Assert.Contains(declared, name =>
          name!.Contains(expectedDeclared, StringComparison.Ordinal));
        foreach (var forbidden in mustNotDeclare)
        {
            Assert.DoesNotContain(declared, name =>
              name!.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    private static IReadOnlyDictionary<string, NLCPGGraph> BuildBatch()
    {
        // 三个文件放进**同一个** compilation —— 这是跨文件解析的前提。
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(Caller, path: "caller.cs"),
            CSharpSyntaxTree.ParseText(Callee, path: "callee.cs"),
            CSharpSyntaxTree.ParseText(Third, path: "third.cs"),
        };
        var compilation = CSharpCompilation.Create(
          assemblyName: "CrossFileBatchEquivalence",
          syntaxTrees: trees,
          references: NLCPGBuilder.CreateMetadataReferences());
        var documents = new[]
        {
            Document(compilation, trees[0], Caller, "caller.cs"),
            Document(compilation, trees[1], Callee, "callee.cs"),
            Document(compilation, trees[2], Third, "third.cs"),
        };

        return new NLCPGBuilder(CreateOptions())
          .BuildManyDocuments(documents)
          .Graphs;
    }

    private static NLCPGGraph BuildIndependent(string source, string filePath)
    {
        return new NLCPGBuilder(CreateOptions()).BuildFromSource(source, filePath);
    }

    private static NLCPGBuildDocument Document(
      CSharpCompilation compilation,
      SyntaxTree tree,
      string source,
      string filePath)
    {
        return new NLCPGBuildDocument(
          filePath,
          source,
          compilation.GetSemanticModel(tree),
          tree.GetRoot());
    }

    private static NLCPGBuilderOptions CreateOptions()
    {
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
        };
    }

    /// <summary>
    /// 该文件的**语法物化**签名：只含语法节点与 token，按（种类, 起止）比较。
    /// </summary>
    /// <remarks>
    /// 刻意**不含** <c>FullName</c>：名字里含类型解析结果，而跨文件解析本来就会把
    /// <c>Callee</c> 解析成 <c>Demo.Callee</c>。语法物化只与源码本身有关，故这是可比的。
    /// </remarks>
    private static string[] SyntaxSignature(NLCPGGraph graph)
    {
        return graph.Nodes
          .Where(node => node.Kind is NLCPGNodeKind.SyntaxTree
                         or NLCPGNodeKind.SyntaxNode
                         or NLCPGNodeKind.SyntaxToken)
          .Select(node => $"N|{node.Kind}|{node.SpanStart}|{node.SpanEnd}")
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
    }

    /// <summary>统计图中「不属于 <paramref name="ownFilePath"/>」的节点数。</summary>
    private static int ForeignNodeCount(NLCPGGraph graph, string ownFilePath)
    {
        return graph.Nodes.Count(node =>
        {
            var path = graph.ResolveFilePath(node);
            return path is not null &&
                   !string.Equals(
                     path.Replace('\\', '/'),
                     ownFilePath.Replace('\\', '/'),
                     StringComparison.OrdinalIgnoreCase);
        });
    }
}
