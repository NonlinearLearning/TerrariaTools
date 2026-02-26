using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using Xunit;

namespace TerrariaTools.UnitTests
{
    public class ShadowGeneratorFunctionalTests
    {
        private async Task<(Dictionary<string, string> Files, Compilation Compilation)> GenerateShadowAsync(string source, string seedTypeName, string seedMethodName)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject", LanguageNames.CSharp);
            var project = workspace.AddProject(projectInfo);

            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var systemCore = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
            var systemRuntime = MetadataReference.CreateFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location), "System.Runtime.dll"));
            var systemCollections = MetadataReference.CreateFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location), "System.Collections.dll"));

            project = project.AddMetadataReference(mscorlib)
                             .AddMetadataReference(systemCore)
                             .AddMetadataReference(systemRuntime)
                             .AddMetadataReference(systemCollections);

            var document = project.AddDocument("Source.cs", source);
            var compilation = await document.Project.GetCompilationAsync();

            var typeSymbol = compilation.GetSymbolsWithName(seedTypeName).OfType<INamedTypeSymbol>().FirstOrDefault();
            ISymbol seedSymbol = typeSymbol;

            if (seedMethodName != null)
            {
                seedSymbol = typeSymbol.GetMembers(seedMethodName).FirstOrDefault();
            }

            var generator = new ShadowClassGenerator(document.Project.Solution);
            var result = await generator.GenerateShadowSourceAsync(seedSymbol);

            return (result, compilation);
        }

        [Fact]
        public async Task Should_Preserve_Static_Field_Initializer_Dependencies()
        {
            // Scenario: WorldGen.Register pattern
            // A static field initializer calls a static method. That method must be preserved.
            var source = @"
using System;
using System.Collections.Generic;

public class WorldGen
{
    public static SecretSeed Register(string name)
    {
        return new SecretSeed(name);
    }
}

public class SecretSeed
{
    public string Name;
    public SecretSeed(string name) { Name = name; }
}

public class Registration
{
    // This static field initializer calls WorldGen.Register
    // ShadowClassGenerator should pick this up as a seed if it scans static fields with initializers.
    public static SecretSeed MySeed = WorldGen.Register(""Test"");

    public static void MainEntry() { }
}
";
            // We seed with MainEntry, but we expect the static field initializer of Registration to be scanned as an extra seed,
            // or at least analyzed because Registration is used?
            // Wait, in the current implementation of ShadowClassGenerator, it explicitly adds ALL static fields with initializers as seeds!
            // So even if MainEntry doesn't use MySeed, MySeed's initializer should be analyzed.

            var (files, _) = await GenerateShadowAsync(source, "Registration", "MainEntry");

            Assert.Contains("Source.cs", files.Keys);
            var code = files["Source.cs"];

            // WorldGen.Register should be present because it's called by the static initializer
            Assert.Contains("Register", code);
            Assert.Contains("SecretSeed", code);
        }

        [Fact]
        public async Task Should_Preserve_Collection_Initializer_Add_Method()
        {
            // Scenario: BiomePreferenceListTrait.Add pattern
            var source = @"
using System;
using System.Collections;
using System.Collections.Generic;

public class BiomePreferenceListTrait : IEnumerable
{
    public void Add(int a, int b) { }
    public IEnumerator GetEnumerator() => throw new NotImplementedException();
}

public class Usage
{
    public void Run()
    {
        // Implicitly calls Add(1, 2)
        var trait = new BiomePreferenceListTrait { { 1, 2 } };
    }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Usage", "Run");
            var code = files["Source.cs"];

            // The Add method must be preserved
            Assert.Contains("public void Add(int a, int b)", code);
        }

        [Fact]
        public async Task Should_Preserve_Constructor_Assigned_Fields_And_Parameters()
        {
            // Scenario: ContentRejectionFromSize pattern
            // Constructor parameters are assigned to fields. Fields should be preserved.
            var source = @"
public class ContentRejection
{
    private int _width;
    private int _height;

    public ContentRejection(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Use() { }
}

public class Usage
{
    public void Run()
    {
        var c = new ContentRejection(800, 600);
        c.Use();
    }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Usage", "Run");
            var code = files["Source.cs"];

            Assert.Contains("_width", code);
            Assert.Contains("_height", code);
            Assert.Contains("int width", code);
        }

        [Fact]
        public async Task Should_Preserve_Implicit_Interface_Implementations()
        {
            // Scenario: CallGraphBuilder interface protection
            // If a class implements an interface, its methods that satisfy the interface should be preserved (or at least kept as stubs),
            // even if they are not directly called, IF the interface itself is passed around or used.
            // But ShadowClassGenerator logic is: if method is not in call graph, it's Delete or None.
            // MemberSlicingRewriter checks IsRequiredByInheritance.

            var source = @"
using System;

public interface IWorker
{
    void DoWork();
}

public class Worker : IWorker
{
    public void DoWork() { Console.WriteLine(""Work""); }
}

public class Usage
{
    public void Run()
    {
        IWorker w = new Worker();
        w.DoWork();
        // Here, Run calls IWorker.DoWork.
        // CodeDependencyAnalyzer should map IWorker.DoWork to Worker.DoWork if it can resolve the type?
        // Or at least, Worker is instantiated, so Worker type is needed.
        // Worker implements IWorker.
        // MemberSlicingRewriter should preserve DoWork because it implements IWorker.DoWork.
    }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Usage", "Run");
            var code = files["Source.cs"];

            Assert.Contains("public void DoWork()", code);
        }

        [Fact]
        public async Task Should_Preserve_Indexer_Left_Hand_Side()
        {
             var source = @"
using System.Collections.Generic;

public class DictionaryHolder
{
    public Dictionary<int, string> _info = new Dictionary<int, string>();

    public void SetInfo(int key, string value)
    {
        _info[key] = value;
    }
}
";
             // Analysis should track _info from _info[key]
             var (files, _) = await GenerateShadowAsync(source, "DictionaryHolder", "SetInfo");
             var code = files["Source.cs"];

             Assert.Contains("_info", code);
        }

        [Fact]
        public async Task Should_Preserve_Generic_Dependencies()
        {
            var source = @"
using System.Collections.Generic;

public class Repository<T>
{
    private List<T> _items = new List<T>();
    public void Add(T item) => _items.Add(item);
}

public class Usage
{
    public void Run()
    {
        var repo = new Repository<string>();
        repo.Add(""test"");
    }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Usage", "Run");
            var code = files["Source.cs"];

            Assert.Contains("class Repository<T>", code);
            Assert.Contains("private List<T> _items", code);
            Assert.Contains("public void Add(T item)", code);
        }

        [Fact]
        public async Task Should_Handle_Async_Await_Dependencies()
        {
            var source = @"
using System.Threading.Tasks;

public class Service
{
    public async Task<int> GetDataAsync()
    {
        await Task.Delay(1);
        return 42;
    }
}

public class Usage
{
    public async Task Run()
    {
        var s = new Service();
        int result = await s.GetDataAsync();
    }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Usage", "Run");
            var code = files["Source.cs"];

            Assert.Contains("async Task<int> GetDataAsync()", code);
            Assert.Contains("await s.GetDataAsync()", code);
        }

        [Fact]
        public async Task Should_Preserve_Inheritance_And_Base_Calls()
        {
            var source = @"
public class BaseClass
{
    public virtual void Setup() { }
}

public class SubClass : BaseClass
{
    public override void Setup()
    {
        base.Setup();
    }
}

public class Usage
{
    public void Run()
    {
        var s = new SubClass();
        s.Setup();
    }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Usage", "Run");
            var code = files["Source.cs"];

            Assert.Contains("class BaseClass", code);
            Assert.Contains("virtual void Setup()", code);
            Assert.Contains("override void Setup()", code);
            Assert.Contains("base.Setup()", code);
        }

        [Fact]
        public async Task Should_Preserve_Main_Path_To_Seed_Only()
        {
            var source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        HelperA.Run();
        HelperB.Unused(); // Should be pruned
    }
}

public class HelperA
{
    public static void Run()
    {
        Seed.Target();
        UnusedA(); // Should be pruned
    }
    public static void UnusedA() { Console.WriteLine(""Unused""); }
}

public class HelperB
{
    public static void Unused() { Console.WriteLine(""Unused""); }
}

public class Seed
{
    public static void Target() { Console.WriteLine(""Target""); }
}
";
            var (files, _) = await GenerateShadowAsync(source, "Seed", "Target");
            var code = files["Source.cs"];

            // Should contain Main, HelperA.Run, Seed.Target
            Assert.Contains("static void Main", code);
            Assert.Contains("class HelperA", code);
            Assert.Contains("void Run()", code);
            Assert.Contains("class Seed", code);
            Assert.Contains("void Target()", code);

            // Should NOT contain HelperB or UnusedA
            Assert.DoesNotContain("class HelperB", code);
            Assert.DoesNotContain("void UnusedA()", code);
            Assert.DoesNotContain("HelperB.Unused()", code);
        }

        [Fact]
        public async Task Should_Resolve_Type_Ambiguity_With_Aliases()
        {
            var source = @"
using System.Drawing;
using Microsoft.Xna.Framework;

public class AmbiguityTest
{
    public void Test()
    {
        // This should be XNA Rectangle due to alias injection
        Rectangle r = new Rectangle(0, 0, 10, 10);
        XnaMethod(r);
    }

    public void XnaMethod(Microsoft.Xna.Framework.Rectangle r) { }
}

namespace System.Drawing {
    public struct Rectangle { public int X, Y, W, H; public Rectangle(int x, int y, int w, int h) { X=x;Y=y;W=w;H=h; } }
    public struct Point { public int X, Y; }
}
namespace Microsoft.Xna.Framework {
    public struct Rectangle { public int X, Y, Width, Height; public Rectangle(int x, int y, int w, int h) { X=x;Y=y;Width=w;Height=h; } }
    public struct Point { public int X, Y; }
}
";
            var (files, _) = await GenerateShadowAsync(source, "AmbiguityTest", "Test");
            var code = files["Source.cs"];
            Console.WriteLine("DEBUG CODE START");
            Console.WriteLine(code);
            Console.WriteLine("DEBUG CODE END");

            // Should contain alias or full qualified name
            Assert.Contains("Microsoft.Xna.Framework.Rectangle", code);
        }

        [Fact]
        public async Task Should_Handle_Custom_Type_Conflict_Gracefully()
        {
            string source = @"
using NsA;

public class Test {
    public void M() {
        var x = new ConflictType();
    }
}
namespace NsA { public class ConflictType {} }
namespace NsB { public class ConflictType {} }
";
            var (files, _) = await GenerateShadowAsync(source, "Test", "M");
            Assert.True(files.ContainsKey("Source.cs"));
            // 验证生成的代码包含 ConflictType
            Assert.Contains("ConflictType", files["Source.cs"]);
        }
    }
}
