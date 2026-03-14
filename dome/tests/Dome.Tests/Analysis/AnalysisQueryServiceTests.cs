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

    [Fact]
    public async Task CreateContext_TreatsEventSubscriptionAsMethodReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System;

                    namespace Sample;

                    public sealed class Player
                    {
                        private event Action? Changed;

                        public Player()
                        {
                            Changed += HandleChanged;
                        }

                        private void HandleChanged()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.Player.HandleChanged()"));
        Assert.Contains(
            context.References.GetReferencingFunctions("Sample.Player.HandleChanged()"),
            memberId => memberId.Value == "Sample.Player..ctor()");
    }

    [Fact]
    public async Task CreateContext_TreatsDelegateFieldAssignmentAsMethodReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System;

                    namespace Sample;

                    public sealed class Player
                    {
                        private readonly Action _handler;

                        public Player()
                        {
                            _handler = HandleChanged;
                        }

                        private void HandleChanged()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.Player.HandleChanged()"));
        Assert.Contains(
            context.References.GetReferencingFunctions("Sample.Player.HandleChanged()"),
            memberId => memberId.Value == "Sample.Player..ctor()");
    }

    [Fact]
    public async Task CreateContext_TreatsGenericRegisterAsTypeReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public sealed class NetManager
                    {
                        public static NetManager Instance { get; } = new();

                        public void Register<T>()
                        {
                        }
                    }

                    internal sealed class NetLiquidModule
                    {
                    }

                    public static class Bootstrap
                    {
                        public static void Load()
                        {
                            NetManager.Instance.Register<NetLiquidModule>();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.NetLiquidModule"));
        Assert.Contains(context.References.GetReferencingTypes("Sample.NetLiquidModule"), typeId => typeId == "Sample.Bootstrap");
    }

    [Fact]
    public async Task CreateContext_TreatsManagerIndexerAssignmentAsTypeReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public sealed class SkyManager
                    {
                        public static SkyManager Instance { get; } = new();

                        public object this[string key]
                        {
                            set
                            {
                            }
                        }
                    }

                    internal sealed class PartySky
                    {
                    }

                    public static class ScreenEffectInitializer
                    {
                        public static void Load()
                        {
                            SkyManager.Instance["Party"] = new PartySky();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.PartySky"));
        Assert.Contains(context.References.GetReferencingTypes("Sample.PartySky"), typeId => typeId == "Sample.ScreenEffectInitializer");
    }

    [Fact]
    public async Task CreateContext_TreatsRuleChainAddAsTypeReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    public interface IItemDropRule
                    {
                    }

                    internal sealed class DropRule : IItemDropRule
                    {
                    }

                    public sealed class LeadingConditionRule
                    {
                        private readonly List<IItemDropRule> ChainedRules = new();

                        public void Add(IItemDropRule rule)
                        {
                            ChainedRules.Add(rule);
                        }
                    }

                    public static class Bootstrap
                    {
                        public static LeadingConditionRule Create()
                        {
                            var chain = new LeadingConditionRule();
                            chain.Add(new DropRule());
                            return chain;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.DropRule"));
        Assert.Contains(context.References.GetReferencingTypes("Sample.DropRule"), typeId => typeId == "Sample.Bootstrap");
    }

    [Fact]
    public async Task CreateContext_DoesNotTreatUnknownInstanceIndexerAssignmentAsTypeReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public sealed class WidgetHolder
                    {
                        public static WidgetHolder Instance { get; } = new();

                        public object this[string key]
                        {
                            set
                            {
                            }
                        }
                    }

                    internal sealed class TempWidget
                    {
                    }

                    public static class Bootstrap
                    {
                        public static void Load()
                        {
                            WidgetHolder.Instance["Temp"] = new TempWidget();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.False(context.References.HasReferences("Sample.TempWidget"));
    }

    [Fact]
    public async Task CreateContext_TreatsExpressionBodiedPropertyAsMethodReference()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public sealed class WorldGenRange
                    {
                        public int Minimum { get; set; }

                        public int ScaledMinimum => ScaleValue(Minimum);

                        private int ScaleValue(int value)
                        {
                            return value * 2;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.References.HasReferences("Sample.WorldGenRange.ScaleValue(int)"));
        Assert.Contains(
            context.References.GetReferencingFunctions("Sample.WorldGenRange.ScaleValue(int)"),
            memberId => memberId.Value == "Sample.WorldGenRange.ScaledMinimum.get");
    }
}
