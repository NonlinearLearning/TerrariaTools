using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeShadowSourceRewriterTests
{
    [Fact]
    public void Rewrite_DefaultsNonVoidMembersAndEmptiesVoidMembers()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Keep()
                {
                    Helper();
                }

                public void DropVoid()
                {
                    Helper();
                }

                public int DropInt()
                {
                    return 42;
                }

                public int Expr => 7;

                public int Value
                {
                    get
                    {
                        return 1;
                    }
                    set
                    {
                        field = value;
                    }
                }

                private void Helper()
                {
                }
            }
            """;

        var (semanticModel, sourceRewriter) = CreateModelAndRewriter(source);
        var result = sourceRewriter.Rewrite(
            source,
            semanticModel,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Sample.Player.Keep()"
            });

        Assert.Contains("Helper();", result.RewrittenSource);
        Assert.Contains("public void DropVoid()", result.RewrittenSource);
        Assert.Contains("public int DropInt()", result.RewrittenSource);
        Assert.Contains("return default;", result.RewrittenSource);
        Assert.DoesNotContain("return 42;", result.RewrittenSource);
        Assert.Contains("public int Expr", result.RewrittenSource);
        Assert.DoesNotContain("=> 7", result.RewrittenSource);
        Assert.Contains("get", result.RewrittenSource);
        Assert.Contains("set", result.RewrittenSource);
        Assert.True(result.Summary.PreservedMembers >= 1);
        Assert.True(result.Summary.DefaultedMembers >= 2);
        Assert.True(result.Summary.EmptiedMembers >= 1);
    }

    [Fact]
    public void Rewrite_PreservesAbstractAndInterfaceMembersWithoutBodies()
    {
        var source = """
            namespace Sample;

            public abstract class BaseThing
            {
                public abstract int Compute();
            }

            public interface IThing
            {
                int Count => 1;

                void Reset()
                {
                }
            }
            """;

        var (semanticModel, sourceRewriter) = CreateModelAndRewriter(source);
        var result = sourceRewriter.Rewrite(source, semanticModel, new HashSet<string>(StringComparer.Ordinal));

        Assert.Contains("public abstract int Compute();", result.RewrittenSource);
        Assert.DoesNotContain("public abstract int Compute()\r\n    {\r\n        return default;", result.RewrittenSource);
        Assert.Contains("int Count", result.RewrittenSource);
        Assert.Contains("get;", result.RewrittenSource);
        Assert.Contains("void Reset()", result.RewrittenSource);
        Assert.DoesNotContain("void Reset()\r\n    {\r\n", result.RewrittenSource);
        Assert.DoesNotContain("return default;", result.RewrittenSource);
    }

    [Fact]
    public void Rewrite_PreservesExternMembersWithoutBodies()
    {
        var source = """
            using System.Runtime.InteropServices;

            namespace Sample;

            public static class NativeMethods
            {
                [DllImport("user32.dll")]
                public static extern int MessageBoxA(nint hWnd, string text, string caption, uint type);
            }
            """;

        var (semanticModel, sourceRewriter) = CreateModelAndRewriter(source);
        var result = sourceRewriter.Rewrite(source, semanticModel, new HashSet<string>(StringComparer.Ordinal));

        Assert.Contains("public static extern int MessageBoxA", result.RewrittenSource);
        Assert.DoesNotContain("return default;", result.RewrittenSource);
        Assert.DoesNotContain("MessageBoxA(nint hWnd, string text, string caption, uint type)\r\n    {\r\n", result.RewrittenSource);
    }

    [Fact]
    public void Rewrite_AssignsOutParametersBeforeReturningDefault()
    {
        var source = """
            namespace Sample;

            public static class Helper
            {
                public static bool TryParse(string text, out int value, out string name)
                {
                    value = 1;
                    name = text;
                    return true;
                }

                public static void Fill(out int left, out int right)
                {
                    left = 1;
                    right = 2;
                }
            }
            """;

        var (semanticModel, sourceRewriter) = CreateModelAndRewriter(source);
        var result = sourceRewriter.Rewrite(source, semanticModel, new HashSet<string>(StringComparer.Ordinal));
        var compact = string.Concat(result.RewrittenSource.Where(static ch => !char.IsWhiteSpace(ch)));

        Assert.Contains("value=default;", compact);
        Assert.Contains("name=default;", compact);
        Assert.Contains("left=default;", compact);
        Assert.Contains("right=default;", compact);
        Assert.Contains("returndefault;", compact);
    }

    private static (SemanticModel SemanticModel, TerrariaRuntimeShadowSourceRewriter Rewriter) CreateModelAndRewriter(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Sample.cs");
        var compilation = CSharpCompilation.Create(
            "ShadowRewriteTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        return (compilation.GetSemanticModel(tree), new TerrariaRuntimeShadowSourceRewriter());
    }
}

