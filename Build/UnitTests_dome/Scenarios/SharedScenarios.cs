namespace TerrariaTools.Dome.Tests.Scenarios;

/// <summary>
/// 共享的测试场景库，用于回归测试和多维验证。
/// </summary>
public static class SharedScenarios
{
    public static readonly string IfElse = @"
        bool condition = true;
        if (condition)
        {
            Console.WriteLine(""True"");
        }
        else
        {
            Console.WriteLine(""False"");
        }
    ";

    public static class IfElseVariants
    {
        public const string SimpleIf = @"
            if (true) { Console.WriteLine(1); }
        ";

        public const string IfElseChain = @"
            if (a) { }
            else if (b) { }
            else { }
        ";

        public const string NestedIf = @"
            if (a) {
                if (b) { }
            }
        ";

        public const string TernaryExpression = @"
            var x = a ? b : c;
        ";
    }

    public static readonly string WhileLoop = @"
        int i = 0;
        while (i < 10)
        {
            i++;
        }
        Console.WriteLine(i);
    ";

    public static class WhileLoopVariants
    {
        public const string SimpleWhile = @"
            while(true) { break; }
        ";

        public const string DoWhile = @"
            do { i++; } while(i < 10);
        ";

        public const string WhileWithContinue = @"
            while(i < 10) { i++; if(i==5) continue; }
        ";

        public const string NestedWhile = @"
            while(a) { while(b) { } }
        ";
    }

    public static class MethodRefactoringScenarios
    {
        public const string VirtualOverridePair = @"
            public class Base { public virtual void M() {} }
            public class Derived : Base {
                public override void M() {}
                public void Caller() { M(); }
            }
        ";

        public const string VirtualWithNoCallers = @"
            public class Base { public virtual void M() { System.Console.WriteLine(1); } }
            public class Derived : Base { public override void M() { System.Console.WriteLine(2); } }
        ";

        public const string InternalPrivatization = @"
            public class T
            {
                public void M() { }
                public void Caller() { M(); }
            }
        ";

        public const string MethodWithOutParameters = @"
            public class Base { public virtual bool M(out string rejectReason) { rejectReason = null; return true; } }
            public class Derived : Base { public override bool M(out string rejectReason) { rejectReason = ""error""; return false; } }
        ";

        public const string MethodWithNumericOutParameters = @"
            public class Base { public virtual void M(out int x) { x = 0; } }
            public class Derived : Base { public override void M(out int x) { x = 1; } }
        ";

        public const string InterfaceUnused = @"
            public interface I { void M(); }
            public class C : I { public void M() { System.Console.WriteLine(1); } }
        ";

        public const string UnusedWithReturnType = @"
            public interface I { int M(); }
            public class C : I { public int M() { return 1; } }
        ";

        public const string UnusedWithAsyncReturnType = @"
            using System.Threading.Tasks;
            public interface I { Task<int> M(); }
            public class C : I { public async Task<int> M() { await Task.Delay(1); return 1; } }
        ";

        public const string UnusedSimple = @"
            class Test {
                void Unused() { }
                public void Used() { Used(); }
            }
        ";

        public const string UnusedWithParameters = @"
            class T { void M(int x, string s) {} }
        ";

        public const string UnusedStatic = @"
            class T { static void M() {} }
        ";

        public const string UnusedAsync = @"
            using System.Threading.Tasks; class T { async Task M() {} }
        ";

        public const string UnusedMultiple = @"
            class T { void M1(){} void M2(){} void M3(){ M3(); } }
        ";

        public const string UnusedPrivate = @"
            class T { private void M() {} }
        ";

        public const string UnusedProtected = @"
            class T { protected void M() {} }
        ";

        public const string UnusedInternal = @"
            class T { internal void M() {} }
        ";

        public const string UnusedGeneric = @"
            class T { void M<T>() {} }
        ";

        public const string UnusedWithAttributes = @"
            class T { [System.Obsolete] void M() {} }
        ";

        public const string UnusedExpressionBodied = @"
            class T { int M() => 1; }
        ";

        public const string UnusedWithXmlDoc = @"
            class T { /// <summary>Doc</summary>
            void M() {} }
        ";

        public const string UnusedNestedClass = @"
            class Outer { class Inner { void M() {} } }
        ";

        public const string UnusedInPartialClass = @"
            partial class T { void M() {} }
        ";

        public const string UnusedWithReturnValues = @"
            class T { int M() { return 1; } }
        ";

        public const string PublicUsedInternally = @"
            namespace N {
                public class T {
                    public void InternalUsed() { }
                    public void Test() { InternalUsed(); }
                }
            }
        ";

        public const string PublicUsedInternallyWithParams = @"
            namespace N { public class T { public void M(int x) {} public void Test() => M(1); } }
        ";

        public const string PublicStaticUsedInternally = @"
            namespace N { public class T { public static void M() {} public void Test() => M(); } }
        ";

    public static class DependencyAnalysisScenarios
    {
        public const string AttributeOnClass = @"
using System;

namespace Test
{
    public class MyAttribute : Attribute { }

    [MyAttribute]
    public class Consumer { }
}";

        public const string AttributeConstructorArgument = @"
using System;

namespace Test
{
    public class Helper { }
    public class MyAttribute : Attribute
    {
        public MyAttribute(Type t) { }
    }

    [MyAttribute(typeof(Helper))]
    public class Consumer { }
}";

        public const string AttributeNamedArgument = @"
using System;

namespace Test
{
    public enum MyEnum { A, B }
    public class MyAttribute : Attribute
    {
        public MyEnum Value { get; set; }
    }

    [MyAttribute(Value = MyEnum.A)]
    public class Consumer { }
}";

        public const string BaseTypeAndInterface = @"
using System;

namespace Test
{
    public class BaseClass { }
    public interface IInterface { }

    public class Consumer : BaseClass, IInterface { }
}";
    }

    public static class ShadowGeneratorScenarios
    {
        public const string StaticFieldInitializerDependencies = @"
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
    public static SecretSeed MySeed = WorldGen.Register(""Test"");
    public static void MainEntry() { }
}
";
    }

        public const string PublicAsyncUsedInternally = @"
            using System.Threading.Tasks; namespace N { public class T { public async Task M() {} public void Test() => M(); } }
        ";

        public const string PublicGenericUsedInternally = @"
            namespace N { public class T { public void M<T>() {} public void Test() => M<int>(); } }
        ";

        public const string MultiplePublicUsedInternally = @"
            namespace N { public class T { public void M1(){} public void M2(){} public void Test(){ M1(); M2(); } } }
        ";

        public const string NamespacePreservation = @"
            namespace MyNamespace { public class T { public void M() {} public void Test() => M(); } }
        ";

        public const string PartialKeywordPreservation = @"
            namespace N { public partial class T { public void M() {} public void Test() => M(); } }
        ";

        public const string FileScopedNamespace = @"
            namespace N; public class T { public void M() {} public void Test() => M(); }
        ";

        public const string ExpressionBodyKeepsIt = @"
            namespace N { public class T { public int M() => 1; public void Test() => M(); } }
        ";

        public const string NestedClassRemainsNested = @"
            namespace N { public class Outer { public class Inner { public void M() {} public void Test() => M(); } } }
        ";

        public const string AttributesKept = @"
            using System; namespace N { public class T { [Obsolete] public void M() {} public void Test() => M(); } }
        ";

        public const string GenericClassKept = @"
            namespace N { public class T<U> { public void M() {} public void Test() => M(); } }
        ";

        public const string InternalUsedInternallyBecomesPrivate = @"
            namespace N { public class T { internal void InternalUsed() {} public void Test() => InternalUsed(); } }
        ";

        public const string ProtectedMethodKept = @"
            namespace N { public class T { protected void ProtUsed() {} public void Test() => ProtUsed(); } }
        ";

        public const string MainMethodKept = @"
            class Program { static void Main(string[] args) {} }
        ";

        public const string ExplicitInterfaceImplementation = @"
            interface I { void M(); } class T : I { void I.M() {} }
        ";

        public const string MultiClassInSameFile = @"
            public class ClassA {
                public void MethodA() { }
            }
            public class ClassB {
                public void MethodB() { }
            }
            public class Caller {
                public void Call(ClassA a, ClassB b) {
                    a.MethodA();
                    b.MethodB();
                }
            }
        ";

        public const string MultiFilePartialClass = @"
            // File1.cs
            namespace N { public partial class T { public void M1() {} } }
            // File2.cs
            namespace N { public partial class T { void Test() => M1(); } }
        ";
    }

    /// <summary>
    /// 核心分析相关场景 (静态分析、特征提取、代码精简)
    /// </summary>
    public static class AnalysisScenarios
    {
        public const string DependencyCycle = @"
            namespace TestNamespace {
                public class ClassA { public ClassB PropB { get; set; } public void MethodA() { } }
                public class ClassB { public ClassC PropC { get; set; } }
                public class ClassC { public ClassA PropA { get; set; } }
            }
        ";

        public const string SlicingPlayer = @"
            public class Player {
                public int UsedField;
                public int UnusedField;
                public void UsedMethod() { UsedField = 1; }
                public void UnusedMethod() { UnusedField = 2; }
            }
        ";

        public const string AmbiguousTypes = @"
            using Microsoft.Xna.Framework;
            using Microsoft.Xna.Framework.Input;
            using System.Windows.Forms;
            using System.Drawing;
            namespace Test {
                public class Demo {
                    public void DoSomething() {
                        Color c;
                        Keys k;
                    }
                }
            }
        ";

        public const string StaticInitializerDependency = @"
            public class WorldGen {
                public static SecretSeed Register(string name) { return new SecretSeed(name); }
            }
            public class SecretSeed {
                public string Name;
                public SecretSeed(string name) { Name = name; }
            }
            public class Registration {
                public static SecretSeed MySeed = WorldGen.Register(""Test"");
                public static void MainEntry() { }
            }
        ";

        public const string PlayerAndMessageBuffer = @"
            namespace Terraria {
                public class Entity { public int whoAmI; }
                public class Player : Entity { public string name; public int difficulty; public int unused; }
            }
        ";

        public const string MessageBufferGetData = @"
            using Terraria;
            namespace Terraria {
                public class MessageBuffer {
                    public void GetData(Player player) {
                        var n = player.name;
                        var d = player.difficulty;
                        var w = player.whoAmI;
                    }
                }
            }
        ";

        public const string CollectionInitializerAdd = @"
            using System.Collections;
            public class BiomePreferenceListTrait : IEnumerable {
                public void Add(int a, int b) { }
                public IEnumerator GetEnumerator() => null;
            }
            public class Usage {
                public void Run() {
                    var trait = new BiomePreferenceListTrait { { 1, 2 } };
                }
            }
        ";

        public const string ConstructorFieldAssignment = @"
            public class ContentRejection {
                private int _width;
                private int _height;
                public ContentRejection(int width, int height) {
                    _width = width;
                    _height = height;
                }
                public void Use() { }
            }
        ";

        public const string EventsAndEventFields = @"
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
            }
        ";

        public const string InterfaceStubs = @"
            using System;
            public interface ITest {
                event EventHandler UnusedEvent;
                int UnusedProperty { get; set; }
            }
            public class TestClass : ITest {
                public event EventHandler UnusedEvent;
                public int UnusedProperty { get; set; }
                public void Seed() { }
            }
        ";

        public const string GenericConstraints = @"
            public interface IConstraint { }
            public class Generic<T> where T : IConstraint { }
            public class TestClass {
                private string[] _array = new string[10];
                public void Seed() {
                    var g = new Generic<IConstraint>();
                    string s = _array[0];
                }
            }
        ";

        public const string AbstractMemberImplementation = @"
            public abstract class Base {
                public abstract void AbstractMethod();
            }
            public class Derived : Base {
                public override void AbstractMethod() { }
                public void Seed() {
                    Base b = this;
                    b.AbstractMethod();
                }
            }
        ";
    }

    public static readonly string ConditionalAccess = @"
        var obj = new { Inner = new { Value = 1 } };
        int? result = obj?.Inner?.Value;
    ";

    public static readonly string MethodGroupOverloads = @"
        public class Test {
            public void Do(int i) { }
            public void Do(string s) { }
            public void Run() {
                Do(1);
                Do(""test"");
            }
        }
    ";

    public static readonly string VariableDeclarator = @"
        int x = 10;
        int y = x + 5;
        Console.WriteLine(y);
    ";

    public static readonly string LogicalAnd = @"
        bool a = true, b = false;
        if (a && b) { }
    ";

    public static readonly string ArithmeticAddition = @"
        int x = 10, y = 20;
        int z = x + y;
    ";

    public static readonly string TryCatchFinally = @"
        try
        {
            Console.WriteLine(""Try"");
        }
        catch (Exception ex)
        {
            Console.WriteLine(""Catch"");
        }
        finally
        {
            Console.WriteLine(""Finally"");
        }
    ";

    public static class ComplexExpressions
    {
        public const string AnonymousObject = @"
            var obj = new { A = 10, B = ""hello"" };
        ";

        public const string TupleExpression = @"
            (int a, string b) t = (1, ""s"");
        ";

        public const string ObjectInitializer = @"
            public class Person { public string Name { get; set; } public int Age { get; set; } }
            public void M() {
                var p = new Person { Name = ""John"", Age = 30 };
            }
        ";

        public const string IsolationBetweenMethods = @"
            public class Test {
                public void Method1() {
                    int v = 1;
                    int x = v + 1;
                }
                public void Method2() {
                    int v = 5;
                    int y = v + 1;
                }
            }
        ";

        public const string ReturnBoolean = @"
            public class PlayerInput { public static bool AllowExecution = true; }
            public class Test {
                public static bool CanExecute() {
                    return PlayerInput.AllowExecution;
                }
            }
        ";

        public const string CascadingAssignment = @"
            public class Test {
                public void M() {
                    int num2 = PlayerInput.SomeValue;
                    if (num2 != 0) { }
                    num2 = PlayerInput.OtherValue;
                    if (num2 != 0) { num2 = 0; }
                }
            }
        ";

        public const string CascadingBinary = @"
            public class Test {
                public void Method() {
                    int num2 = PlayerInput.A - PlayerInput.B;
                    if (num2 != 0) {
                        System.Console.WriteLine(num2);
                    }
                }
            }
        ";
    }

    public static class TerrariaConditions
    {
        public const string SimpleNetMode = @"
            public class Test {
                public int netMode;
                void Do() { }
                void Method() {
                    if (this.netMode == 1) { Do(); }
                }
            }
        ";

        public const string LiteralOnRight = @"
            public class Test {
                public int netMode;
                void Do() { }
                void Method() {
                    if (1 == netMode) { Do(); }
                }
            }
        ";

        public const string MainNetMode = @"
            namespace Terraria {
                public class Main {
                    public static int netMode;
                }
            }
            class Test {
                void Do() { }
                void Method() {
                    if (Terraria.Main.netMode == 1) { Do(); }
                }
            }
        ";

        public const string AndConditions = @"
            public class Test {
                public int netMode;
                public bool A, B;
                void Do() { }
                void Method() {
                    if (A && netMode == 1 && B) { Do(); }
                }
            }
        ";

        public const string OrConditions = @"
            public class Test {
                public int netMode;
                public bool A, B;
                void Do() { }
                void Method() {
                    if (A || netMode == 1 || B) { Do(); }
                }
            }
        ";

        public const string IfElsePromote = @"
            public class Test {
                public int netMode;
                void ClientOnly() { }
                void ServerOnly() { }
                void Method() {
                    if (netMode == 1) { ClientOnly(); } else { ServerOnly(); }
                }
            }
        ";

        public const string IfElseIfPromote = @"
            public class Test {
                public int netMode;
                public bool A;
                void ClientOnly() { }
                void DoSomething() { }
                void Method() {
                    if (netMode == 1) { ClientOnly(); } else if (A) { DoSomething(); }
                }
            }
        ";
    }

    public static class ImplicitDependencyScenarios
    {
        public const string ForEachEnumerator = @"
using System;
using System.Collections.Generic;

namespace Test
{
    public class MyEnumerator
    {
        public int Current => 0;
        public bool MoveNext() => false;
    }

    public class MyCollection
    {
        public MyEnumerator GetEnumerator() => new MyEnumerator();
    }

    public class Consumer
    {
        public void Consume()
        {
            var c = new MyCollection();
            foreach (var i in c) { }
        }
    }
}";

        public const string UsingDispose = @"
using System;

namespace Test
{
    public class MyResource : IDisposable
    {
        public void Dispose() { }
    }

    public class Consumer
    {
        public void Use()
        {
            using (var r = new MyResource()) { }
        }
    }
}";

        public const string LinqQuery = @"
using System;
using System.Linq;
using System.Collections.Generic;

namespace Test
{
    public class Consumer
    {
        public void Query()
        {
            var list = new List<int>();
            var q = from x in list
                    where x > 0
                    select x.ToString();
        }
    }
}";

        public const string AttributeDependency = @"
using System;

namespace Test
{
    public class MyAttrAttribute : Attribute
    {
        public MyAttrAttribute(Type t) {}
    }

    public class DepClass {}

    [MyAttr(typeof(DepClass))]
    public class Consumer {}
}";
    }

    public static class OperatorDependencyScenarios
    {
        public const string BinaryOperator = @"
using System;

namespace Test
{
    public class Vector2
    {
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2();
    }

    public class Consumer
    {
        public void Method()
        {
            var v1 = new Vector2();
            var v2 = new Vector2();
            var v3 = v1 + v2;
        }
    }
}";

        public const string Deconstruction = @"
using System;

namespace Test
{
    public class Point
    {
        public void Deconstruct(out int x, out int y)
        {
            x = 0;
            y = 0;
        }
    }

    public class Consumer
    {
        public void Method()
        {
            var p = new Point();
            var (x, y) = p;
        }
    }
}";

        public const string AssignmentDeconstruction = @"
using System;

namespace Test
{
    public class Point
    {
        public void Deconstruct(out int x, out int y)
        {
            x = 0;
            y = 0;
        }
    }

    public class Consumer
    {
        public void Method()
        {
            var p = new Point();
            int x, y;
            (x, y) = p;
        }
    }
}";

        public const string ForeachDeconstruction = @"
using System;
using System.Collections.Generic;

namespace Test
{
    public class Point
    {
        public void Deconstruct(out int x, out int y)
        {
            x = 0;
            y = 0;
        }
    }

    public class Consumer
    {
        public void Method()
        {
            var points = new List<Point> { new Point() };
            foreach (var (x, y) in points)
            {
            }
        }
    }
}";
    }
}
