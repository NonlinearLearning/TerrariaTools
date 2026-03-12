using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 成员ID构建器测试类。
/// </summary>
public class MemberIdBuilderTests
{
    /// <summary>
    /// 测试构建方法ID使用元数据签名处理重载。
    /// </summary>
    [Fact]
    public void BuildMethodId_UsesMetadataSignatureForOverloads()
    {
        const string source = """
            namespace Sample;

            public class Calculator
            {
                public int Add(int left, int right) => left + right;
                public string Add(string left, string right) => left + right;
            }
            """;

        var compilation = CreateCompilation(source);
        var root = compilation.SyntaxTrees.Single().GetCompilationUnitRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var model = compilation.GetSemanticModel(root.SyntaxTree);

        var left = model.GetDeclaredSymbol(methods[0])!;
        var right = model.GetDeclaredSymbol(methods[1])!;

        var leftId = MetadataMemberIdBuilder.Build(left);
        var rightId = MetadataMemberIdBuilder.Build(right);

        Assert.Equal("Sample.Calculator.Add(int, int)", leftId.Value);
        Assert.Equal("Sample.Calculator.Add(string, string)", rightId.Value);
    }

    /// <summary>
    /// 测试构建访问器ID包含属性访问器类型。
    /// </summary>
    [Fact]
    public void BuildAccessorId_IncludesPropertyAccessorKind()
    {
        const string source = """
            namespace Sample;

            public class Player
            {
                public int Health
                {
                    get { return 100; }
                    set { }
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var root = compilation.SyntaxTrees.Single().GetCompilationUnitRoot();
        var accessors = root.DescendantNodes().OfType<AccessorDeclarationSyntax>().ToArray();
        var model = compilation.GetSemanticModel(root.SyntaxTree);

        var getter = model.GetDeclaredSymbol(accessors[0])!;
        var setter = model.GetDeclaredSymbol(accessors[1])!;

        Assert.Equal("Sample.Player.Health.get", MetadataMemberIdBuilder.Build(getter).Value);
        Assert.Equal("Sample.Player.Health.set", MetadataMemberIdBuilder.Build(setter).Value);
    }

    /// <summary>
    /// 创建编译单元。
    /// </summary>
    /// <param name="source">源代码。</param>
    /// <returns>C#编译单元。</returns>
    private static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "MemberIds",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
    }
}
