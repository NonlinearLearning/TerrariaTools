using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 分析查询服务测试类。
/// </summary>
public class AnalysisQueryServiceTests
{
    /// <summary>
    /// 测试创建上下文构建继承和引用查询。
    /// </summary>
    [Fact]
    public async Task CreateContext_BuildsInheritanceAndReferenceQueries()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "PlayerBase.cs",
                    "PlayerBase.cs",
                    """
                    namespace Sample;

                    public interface IRunner
                    {
                        int Run(int seed);
                    }

                    public abstract class PlayerBase : IRunner
                    {
                        public abstract int Run(int seed);
                    }
                    """),
                new SourceDocument(
                    "Player.cs",
                    "Player.cs",
                    """
                    namespace Sample;

                    public class Player : PlayerBase
                    {
                        public override int Run(int seed)
                        {
                            return Normalize(seed);
                        }

                        private int Normalize(int value)
                        {
                            return value;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.Inheritance.IsOverrideMember("Sample.Player.Run(int)"));
        Assert.True(context.Inheritance.ImplementsInterfaceMember("Sample.Player.Run(int)"));
        Assert.True(context.References.HasReferences("Sample.Player.Normalize(int)"));
        Assert.Contains(
            context.References.GetReferencingFunctions("Sample.Player.Normalize(int)"),
            memberId => memberId.Value == "Sample.Player.Run(int)");
        Assert.Contains(
            context.References.GetReferencingTypes("Sample.Player.Normalize(int)"),
            typeId => typeId == "Sample.Player");
    }

    /// <summary>
    /// 测试创建上下文为顶级类构建类型引用查询。
    /// </summary>
    [Fact]
    public async Task CreateContext_BuildsTypeReferenceQueriesForTopLevelClasses()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "CacheEntry.cs",
                    "CacheEntry.cs",
                    """
                    namespace Sample;

                    class CacheEntry
                    {
                        public int Value { get; set; }
                    }
                    """),
                new SourceDocument(
                    "Player.cs",
                    "Player.cs",
                    """
                    namespace Sample;

                    public class Player
                    {
                        private readonly CacheEntry _entry = new();
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.CacheEntry"));
        Assert.Contains(context.References.GetReferencingTypes("Sample.CacheEntry"), typeId => typeId == "Sample.Player");
    }
}
