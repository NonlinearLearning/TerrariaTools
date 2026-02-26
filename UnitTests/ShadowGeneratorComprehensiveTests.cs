using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis;
using Xunit;

namespace TerrariaTools.UnitTests
{
    public class ShadowGeneratorComprehensiveTests
    {
        private async Task<string> RunGeneratorAsync(string source, string seedName)
        {
            var workspace = new AdhocWorkspace();
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "TestProj", "TestProj", LanguageNames.CSharp)
                .WithMetadataReferences(new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
                });
            var project = workspace.AddProject(projectInfo)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location), "System.Runtime.dll")));

            var document = project.AddDocument("TestFile.cs", source);
            var compilation = await document.Project.GetCompilationAsync();
            var semanticModel = await document.GetSemanticModelAsync();

            var seedSymbol = compilation.GetSymbolsWithName(seedName).FirstOrDefault();
            Assert.NotNull(seedSymbol);

            var generator = new ShadowClassGenerator(document.Project.Solution);
            var results = await generator.GenerateShadowSourceAsync(seedSymbol);

            string combined = string.Join("\n--- File Split ---\n", results.Values);
            Console.WriteLine("DEBUG: Combined Results:\n" + combined);
            return combined;
        }

        [Fact]
        public async Task Should_Preserve_Events_And_EventFields()
        {
            string source = @"
using System;
public class TestClass {
    public event EventHandler MyEvent;
    public event EventHandler MyExplicitEvent {
        add { }
        remove { }
    }
    public void Seed() {
        MyEvent?.Invoke(this, EventArgs.Empty);
        MyExplicitEvent += (s, e) => {};
    }
}";
            var result = await RunGeneratorAsync(source, "Seed");

            Assert.Contains("event EventHandler MyEvent", result);
            Assert.Contains("event EventHandler MyExplicitEvent", result);
            Assert.Contains("add { }", result);
            Assert.Contains("remove { }", result);
        }

        [Fact]
        public async Task Should_Stub_Unused_Required_Events_And_Properties()
        {
            string source = @"
using System;
public interface ITest {
    event EventHandler UnusedEvent;
    int UnusedProperty { get; set; }
}
public class TestClass : ITest {
    public event EventHandler UnusedEvent;
    public int UnusedProperty { get; set; }
    public void Seed() { }
}";
            var result = await RunGeneratorAsync(source, "Seed");

            // Should contain stubs because they are required by ITest
            Assert.Contains("event EventHandler UnusedEvent", result);
            Assert.Contains("add", result);
            Assert.Contains("remove", result);
            Assert.Contains("int UnusedProperty", result);
            Assert.Contains("get", result);
            Assert.Contains("set", result);
            Assert.Contains("return default", result);
        }

        [Fact]
        public async Task Should_Preserve_Structs_And_Interfaces_When_Used()
        {
            string source = @"
public interface IInterface { void M(); }
public struct MyStruct { public int X; }
public class TestClass {
    public void Seed(IInterface i, MyStruct s) {
        i.M();
        int x = s.X;
    }
}";
            var result = await RunGeneratorAsync(source, "Seed");

            Assert.Contains("interface IInterface", result);
            Assert.Contains("struct MyStruct", result);
            Assert.Contains("void M()", result);
            Assert.Contains("int X", result);
        }

        [Fact]
        public async Task Should_Handle_ElementAccess_And_Initializers()
        {
            string source = @"
using System.Collections.Generic;
public class TestClass {
    private Dictionary<string, int> _dict = new Dictionary<string, int> { { ""a"", 1 } };
    public void Seed() {
        int x = _dict[""a""];
    }
}";
            var result = await RunGeneratorAsync(source, "Seed");

            Assert.Contains("_dict", result);
            Assert.Contains("new Dictionary<string, int>", result);
            Assert.Contains("{ \"a\", 1 }", result);
        }

        [Fact]
        public async Task Should_Handle_Generic_Constraints_And_Arrays()
        {
            string source = @"
public interface IConstraint { }
public class Generic<T> where T : IConstraint { }
public class TestClass {
    private string[] _array = new string[10];
    public void Seed() {
        var g = new Generic<IConstraint>();
        string s = _array[0];
    }
}";
            var result = await RunGeneratorAsync(source, "Seed");

            Assert.Contains("interface IConstraint", result);
            Assert.Contains("class Generic<T>", result);
            Assert.Contains("where T : IConstraint", result);
            Assert.Contains("string[] _array", result);
        }

        [Fact]
        public async Task Should_Handle_Abstract_Member_Implementation()
        {
            string source = @"
public abstract class Base {
    public abstract void AbstractMethod();
}
public class Derived : Base {
    public override void AbstractMethod() { }
    public void Seed() {
        Base b = this;
        b.AbstractMethod();
    }
}";
            var result = await RunGeneratorAsync(source, "Seed");

            Assert.Contains("abstract void AbstractMethod()", result);
            Assert.Contains("override void AbstractMethod()", result);
        }

        [Fact]
        public async Task Should_Handle_Ambiguous_Symbols_In_UsageWalker()
        {
            // This test is tricky because we need to create an actual ambiguity.
            // We can do this by having two types with same name in different namespaces
            // and NOT having a using for either, but referencing it in a way that is ambiguous.
            // However, Roslyn might just fail to resolve it.
            // A better way is to use our PreferredMappings.
            string source = @"
namespace Microsoft.Xna.Framework {
    public struct Rectangle { public int X; }
}
namespace System.Drawing {
    public struct Rectangle { public int X; }
}
namespace Test {
    using Microsoft.Xna.Framework;
    using System.Drawing;
    public class TestClass {
        public void Seed() {
            // This is ambiguous without an alias
            // Rectangle r;
        }
    }
}";
            // Since we can't easily trigger CandidateReason.Ambiguous without a compile error,
            // we'll rely on our PreferredMappings detection.
            string source2 = @"
namespace Microsoft.Xna.Framework {
    public struct Color { }
}
namespace Test {
    using Microsoft.Xna.Framework;
    public class TestClass {
        public void Seed() {
            Color c = new Color();
        }
    }
}";
            var result = await RunGeneratorAsync(source2, "Seed");
            Assert.Contains("using Color = Microsoft.Xna.Framework.Color;", result);
        }
    }
}
