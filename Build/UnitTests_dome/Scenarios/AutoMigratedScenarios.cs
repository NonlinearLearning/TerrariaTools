namespace TerrariaTools.UnitTests.Scenarios;

public static class AutoMigratedScenarios
{
    public const string BugFixTests_Source_1 = @"
public class Seed { public Seed(string code) {} }
public class WorldGen
{
    public static Seed paintEverythingGray = Register(""code"");
    public static Seed Register(string code) => new Seed(code);
}";

    public const string DependencyAnalyzerDebugTests_Source_1 = @"
using System;
using System.Collections;
public class MyCollection : IEnumerable
{
    public void Add(int i) { }
    public void Add(string s, int i) { }
    public IEnumerator GetEnumerator() => throw new NotImplementedException();
}
public class TestClass
{
    public void Run()
    {
        var c = new MyCollection { 1, { ""key"", 2 } };
    }
}";

    public const string DependencyAnalyzerTests_Fixes_Source_1 = @"
using System;
using System.Collections;
public class MyCollection : IEnumerable
{
    public void Add(int i) { }
    public void Add(string s, int i) { }
    public IEnumerator GetEnumerator() => null;
}
public class TestClass
{
    public static int StaticField;
    private int _field;
    static TestClass() { StaticField = 42; }
    public TestClass(int value) { _field = value; }
    public void Run()
    {
        var instance = new TestClass(10);
        var c = new MyCollection { 1, { ""k"", 2 } };
    }
}";

    public const string ExpressionPipelineTests_Source_1 = @"
using System;
[MyAttr(1, ""keep"")]
public class C
{
    void SpawnMinionOnCursor(string s, int a, int damage, int originalDamage, float f, int v1 = 0, int v2 = 0) { }
    void M(string s, int v)
    {
        SpawnMinionOnCursor(s, 1, 2, 3, 4.0f, v, v);
        int num2 = PlayerInput.A - 10;
        if (num2 != 0) { }
        int num = 0;
        if (num2 != 0) { num += num2; }
        bool flag = PlayerInput.MouseInControl;
        if (flag) { PlayerInput.HotbarScrollCD = 0; }
        if (!MouseRight && !Main.playerInventory) { PlayerInput.MouseInControl = false; }
    }
    bool MouseRight;
}
public static class PlayerInput
{
    public static int SomeValue;
    public static int OtherValue;
    public static int A;
    public static bool MouseInControl;
    public static int HotbarScrollCD;
}
public static class Main { public static bool playerInventory; }
public sealed class MyAttrAttribute : Attribute
{
    public MyAttrAttribute(int n, string s) { }
}";

    public const string HybridAtomicMiddlewareTests_Source_1 = @"
using System;
public class C
{
    public int M()
    {
        while (true) break;
        Console.WriteLine(1);
        Console.WriteLine(""core"");
        return 1;
    }
}";

    public const string HybridContextQueryApiTests_Source_1 = @"
using System;
public class C
{
    public void M(int p)
    {
        int x = p;
        Console.WriteLine(x);
        Console.WriteLine(x);
        int y = 0;
    }
}";

    public const string HybridDefUseAnalysisTests_Source_1 = @"
public class C
{
    public int M(int p)
    {
        int x = p;
        x = x + 1;
        int y = 0;
        {
            int x = 2;
            return x;
        }
    }
}";

    public const string HybridMruPlanningTests_Source_1 = @"
using System;
public class C
{
    public void M(bool b)
    {
        if (b) { Console.WriteLine(1); }
        while (b) { break; }
    }
}";

    public const string HybridRuleEngineEnhancementTests_Source_1 = @"
using System;
public interface IFoo { }
[Obsolete]
public class C : IFoo
{
    public int M(int x)
    {
        if (x > 0) x = x + 1;
        switch (x)
        {
            case 1: return x;
            default: return 0;
        }
    }
}";

    public const string HybridUtilityMiddlewareTests_Source_1 = @"
using System;
public class C
{
    public int M()
    {
        // keep-me
        Console.WriteLine(1); // tail
        Console.WriteLine(2);
        return 1;
    }
}";

    public const string NameBasedMethodRefactorerTests_callerSource_1 = @"
public class Caller
{
    public void M(Target t) { t.DrawMe(); }
}";

    public const string NameBasedMethodRefactorerTests_Source_1 = @"
using System;
public class Target
{
    public void DrawMe() { Console.WriteLine(1); }
    public void KeepMe() { Console.WriteLine(2); }
}";

    public const string NameBasedMethodRefactorerTests_targetSource_1 = @"
using System;
public class Target
{
    public void DrawMe() { Console.WriteLine(1); }
}";

    public const string PlayerFieldExtractorTests_bufferSource_1 = @"
using Terraria;
namespace Terraria
{
    public class MessageBuffer
    {
        public void GetData(Player player)
        {
            var n = player.name;
            var d = player.difficulty;
            var w = player.whoAmI;
        }
    }
}";

    public const string PlayerFieldExtractorTests_playerSource_1 = @"
namespace Terraria
{
    public class Entity { public int whoAmI; }
    public class Player : Entity
    {
        public string name;
        public int difficulty;
        public int unused;
    }
}";

    public const string RoslynatorAnalysisAdapterTests_Source_1 = @"
using System;
public class C
{
    public int M(int x)
    {
        if (x > 0) x++;
        for (int i = 0; i < 3; i++) x += i;
        return x switch { 0 => 0, 1 => 1, _ => Inline(x) };
    }
    private int Inline(int v) => v + 1;
}";

    public const string ShadowGeneratorComprehensiveTests_Source_1 = @"
using System;
using System.Collections.Generic;
public interface IInterface { void M(); }
public struct MyStruct : IInterface { public int X { get; set; } public void M() { } }
public interface ITest { event EventHandler UnusedEvent; int UnusedProperty { get; set; } }
public interface IConstraint { }
public class Generic<T> where T : IConstraint { }
public abstract class Base { public abstract void AbstractMethod(); }
public class Derived : Base { public override void AbstractMethod() { } }
public class Seed : ITest
{
    public event EventHandler MyEvent;
    public event EventHandler MyExplicitEvent { add { } remove { } }
    public event EventHandler UnusedEvent { add { } remove { } }
    public int UnusedProperty { get; set; }
    private Dictionary<string, int> _dict = new Dictionary<string, int> { { ""a"", 1 } };
    private string[] _array = new string[10];
    public void Run()
    {
        var s = new MyStruct { X = 1 };
        s.M();
        var g = new Generic<IConstraint>();
        Base b = new Derived();
        b.AbstractMethod();
        _dict[""a""] = 2;
        var t = _array[0];
        MyEvent?.Invoke(this, EventArgs.Empty);
    }
}";

    public const string ShadowGeneratorComprehensiveTests_source2_1 = @"
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Windows.Forms;
using System.Drawing;
public class Seed
{
    public void Run()
    {
        Color c;
        Keys k;
    }
}";

    public const string ShadowGeneratorTests_Source_1 = @"
public class WorldGen
{
    public static SecretSeed Register(string name) => new SecretSeed(name);
}
public class SecretSeed
{
    public string Name;
    public SecretSeed(string name) { Name = name; }
}
public class Registration
{
    public static SecretSeed MySeed = WorldGen.Register(""Test"");
    public static void MainEntry() { }
}";

    public const string SlicingRewriterTests_drawingStub_1 = @"
namespace System.Drawing
{
    public struct Color { }
}";

    public const string SlicingRewriterTests_formsStub_1 = @"
namespace System.Windows.Forms
{
    public enum Keys { A }
}";

    public const string SlicingRewriterTests_sourceCode_1 = @"
public class Player
{
    public int UsedField;
    public int UnusedField;
    public void UsedMethod() { UsedField = 1; }
    public void UnusedMethod() { UnusedField = 2; }
}";

    public const string SlicingRewriterTests_sourceCode_2 = @"
using Microsoft.Xna.Framework;
using System.Windows.Forms;
using System.Drawing;
namespace Test
{
    public class Demo
    {
        public void DoSomething()
        {
            Color c;
            Keys k;
        }
    }
}";

    public const string SlicingRewriterTests_xnaStub_1 = @"
namespace Microsoft.Xna.Framework
{
    public struct Color { }
}
namespace Microsoft.Xna.Framework.Input
{
    public enum Keys { A }
}";

    public const string NameBasedMethodRefactorerTests_baseSource_1 = @"
public class Base {}
";

    public const string NameBasedMethodRefactorerTests_derivedSource_1 = @"
public sealed class Derived : Base { public void DrawMe() {} }
";
}
