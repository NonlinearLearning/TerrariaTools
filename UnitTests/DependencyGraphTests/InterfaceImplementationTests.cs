using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using TerrariaTools.Analysis;

namespace UnitTests.DependencyGraphTests
{
    public class InterfaceImplementationTests
    {
        [Fact]
        public async Task ShouldKeep_AbstractInterfaceImplementation()
        {
            string source = @"
using System.Collections.Generic;
namespace Test
{
    public interface IEntry { }
    public interface ICreativeItemSortStep : IEntry { }
    public interface IEntrySortStep<T> : IComparer<T> { }

    public abstract class ACreativeItemSortStep : ICreativeItemSortStep, IEntrySortStep<object>, IComparer<object>
    {
        public abstract int Compare(object x, object y);
    }

    public class ByUnlockStatus : ACreativeItemSortStep
    {
        public override int Compare(object x, object y) => 0;
    }

    public class Program
    {
        public static void Main()
        {
            var sorter = new ByUnlockStatus();
            // No direct call to Compare
        }
    }
}";

            var results = await AnalyzeAsync(source);

            // Find the symbols for ACreativeItemSortStep.Compare and ByUnlockStatus.Compare
            var abstractCompare = results.Keys.FirstOrDefault(m => m.ContainingType.Name == "ACreativeItemSortStep" && m.Name == "Compare");
            var overrideCompare = results.Keys.FirstOrDefault(m => m.ContainingType.Name == "ByUnlockStatus" && m.Name == "Compare");

            // ACreativeItemSortStep.Compare is abstract, it should be GraphMethodAction.None (not in results)
            if (abstractCompare != null)
            {
                Assert.True(!results.ContainsKey(abstractCompare) || results[abstractCompare] == CallGraphBuilder.GraphMethodAction.None);
            }

            // ByUnlockStatus.Compare is an interface implementation, it should be GraphMethodAction.ClearBody (at worst) or None
            // If it is deleted, that's a bug.
            if (overrideCompare != null)
            {
                Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[overrideCompare]);
            }
        }

        [Fact]
        public async Task ShouldKeep_InheritedInterfaceImplementation()
        {
            string source = @"
namespace Test
{
    public class SpriteBatch { }
    public class Item { }
    public struct Vector2 { public float X, Y; }

    public abstract class UIDynamicItemCollection<T>
    {
        protected abstract T GetItem(T entry);
        protected abstract void DrawSlot(SpriteBatch spriteBatch, T item, Vector2 pos, bool hovering);
    }

    public class UICreativeItemGrid : UIDynamicItemCollection<Item>
    {
        protected override Item GetItem(Item entry) => entry;
        protected override void DrawSlot(SpriteBatch spriteBatch, Item item, Vector2 pos, bool hovering) { }
    }

    public class Program
    {
        public static void Main()
        {
            var grid = new UICreativeItemGrid();
            // No direct call to GetItem or DrawSlot
        }
    }
}";

            var results = await AnalyzeAsync(source);

            var getItem = results.Keys.FirstOrDefault(m => m.ContainingType.Name == "UICreativeItemGrid" && m.Name == "GetItem");
            var drawSlot = results.Keys.FirstOrDefault(m => m.ContainingType.Name == "UICreativeItemGrid" && m.Name == "DrawSlot");

            if (getItem != null)
            {
                Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[getItem]);
            }
            if (drawSlot != null)
            {
                Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[drawSlot]);
            }
        }

        [Fact]
        public async Task ShouldKeep_ExplicitInterfaceImplementation()
        {
            string source = @"
namespace Test
{
    public interface ITest { void M(); }
    public class C : ITest
    {
        void ITest.M() { }
    }
    public class Program
    {
        public static void Main() { var c = new C(); }
    }
}";
            var results = await AnalyzeAsync(source);
            var m = results.Keys.FirstOrDefault(k => k.Name.EndsWith(".M")); // Explicit implementations have names like ITest.M

            if (m != null)
            {
                Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[m]);
            }
        }

        [Fact]
        public async Task ShouldKeep_GenericInterfaceImplementation()
        {
            string source = @"
namespace Test
{
    public interface IGeneric<T> { void Process(T item); }
    public class IntProcessor : IGeneric<int>
    {
        public void Process(int item) { }
    }
    public class Program
    {
        public static void Main() { var p = new IntProcessor(); }
    }
}";
            var results = await AnalyzeAsync(source);
            var m = results.Keys.FirstOrDefault(k => k.ContainingType.Name == "IntProcessor" && k.Name == "Process");

            if (m != null)
            {
                Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[m]);
            }
        }

        [Fact]
        public async Task ShouldKeep_MultipleInterfaceImplementation()
        {
            string source = @"
namespace Test
{
    public interface IA { void A(); }
    public interface IB { void B(); }
    public class Multi : IA, IB
    {
        public void A() { }
        public void B() { }
    }
    public class Program
    {
        public static void Main() { var m = new Multi(); }
    }
}";
            var results = await AnalyzeAsync(source);
            var ma = results.Keys.FirstOrDefault(k => k.ContainingType.Name == "Multi" && k.Name == "A");
            var mb = results.Keys.FirstOrDefault(k => k.ContainingType.Name == "Multi" && k.Name == "B");

            if (ma != null) Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[ma]);
            if (mb != null) Assert.NotEqual(CallGraphBuilder.GraphMethodAction.Delete, results[mb]);
        }

        private async Task<Dictionary<IMethodSymbol, CallGraphBuilder.GraphMethodAction>> AnalyzeAsync(string source)
        {
            var adhocWorkspace = new AdhocWorkspace();
            var solution = adhocWorkspace.CurrentSolution;
            var projectId = ProjectId.CreateNewId();
            var version = VersionStamp.Create();

            // Add necessary references
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var linq = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);

            var projectInfo = ProjectInfo.Create(projectId, version, "TestProject", "TestProject", LanguageNames.CSharp)
                .WithMetadataReferences(new[] { mscorlib, linq });

            solution = solution.AddProject(projectInfo);
            var documentId = DocumentId.CreateNewId(projectId);
            solution = solution.AddDocument(documentId, "Test.cs", source);

            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync();
            return builder.AnalyzeMethods(aggressive: true, enableRatioAnalysis: true);
        }
    }
}
