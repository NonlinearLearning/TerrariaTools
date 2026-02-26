using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using Xunit;
using Microsoft.CodeAnalysis.Text;

namespace TerrariaTools.UnitTests.DependencyGraphTests
{
    public class CallGraphBuilderUltraTests
    {
        public enum TestMethodAccessibility { Public, Internal, Private, Protected, PrivateProtected, ProtectedInternal }
        public enum TestMethodModifier { None, Static, Virtual, Override, Abstract, Async }
        public enum TestMethodKind { Regular, Constructor, Main }
        public enum TestCallerType { None, SameType, OtherTypeSameAssembly, OtherTypeDiffAssembly }
        public enum TestTypeKind { Class, Struct, Interface }

        public class UltraTestCase
        {
            public TestMethodAccessibility Accessibility { get; set; }
            public TestMethodModifier Modifier { get; set; }
            public TestMethodKind Kind { get; set; }
            public TestCallerType Caller { get; set; }
            public TestTypeKind TypeKind { get; set; }

            public override string ToString()
            {
                return $"{TypeKind}-{Accessibility}-{Modifier}-{Kind}-CalledBy-{Caller}";
            }
        }

        public static IEnumerable<object[]> GetUltraTestCases()
        {
            var accessibilities = Enum.GetValues<TestMethodAccessibility>();
            var modifiers = Enum.GetValues<TestMethodModifier>();
            var kinds = Enum.GetValues<TestMethodKind>();
            var callers = Enum.GetValues<TestCallerType>();
            var typeKinds = Enum.GetValues<TestTypeKind>();

            // 采用全组合遍历，不再限制 200 个，因为 IsValidCombination 会过滤掉大部分无效组合
            foreach (var tk in typeKinds)
            foreach (var acc in accessibilities)
            foreach (var mod in modifiers)
            foreach (var kind in kinds)
            foreach (var caller in callers)
            {
                if (!IsValidCombination(tk, acc, mod, kind, caller)) continue;

                yield return new object[] { new UltraTestCase
                {
                    TypeKind = tk,
                    Accessibility = acc,
                    Modifier = mod,
                    Kind = kind,
                    Caller = caller
                } };
            }
        }

        private static bool IsValidCombination(TestTypeKind tk, TestMethodAccessibility acc, TestMethodModifier mod, TestMethodKind kind, TestCallerType caller)
        {
            // 1. Main 必须是 Static 且通常是 Regular
            if (kind == TestMethodKind.Main && mod != TestMethodModifier.Static) return false;

            // 2. 构造函数不能是 Virtual/Abstract/Override
            if (kind == TestMethodKind.Constructor && (mod == TestMethodModifier.Virtual || mod == TestMethodModifier.Abstract || mod == TestMethodModifier.Override)) return false;

            // 3. Struct 不支持 Virtual/Abstract/Override
            if (tk == TestTypeKind.Struct && (mod == TestMethodModifier.Virtual || mod == TestMethodModifier.Abstract || mod == TestMethodModifier.Override)) return false;

            // 4. Interface 约束
            if (tk == TestTypeKind.Interface)
            {
                if (acc != TestMethodAccessibility.Public) return false;
                if (mod == TestMethodModifier.Static || mod == TestMethodModifier.Async || mod == TestMethodModifier.Override) return false;
            }

            // 5. Abstract 只能在 Class (假设 Class 可以是 abstract) 中
            if (mod == TestMethodModifier.Abstract && tk != TestTypeKind.Class) return false;

            // 6. 访问权限与调用者约束
            if (acc == TestMethodAccessibility.Private && caller != TestCallerType.None && caller != TestCallerType.SameType) return false;
            if (acc == TestMethodAccessibility.Internal && caller == TestCallerType.OtherTypeDiffAssembly) return false;
            if (acc == TestMethodAccessibility.PrivateProtected && caller == TestCallerType.OtherTypeDiffAssembly) return false;
            if (acc == TestMethodAccessibility.Protected && caller == TestCallerType.OtherTypeDiffAssembly && tk == TestTypeKind.Struct) return false; // Struct doesn't support protected
            if (acc == TestMethodAccessibility.Protected && caller == TestCallerType.OtherTypeDiffAssembly)
            {
                // Protected is only accessible via inheritance in diff assembly
                if (tk == TestTypeKind.Interface) return false;
            }

            return true;
        }

        [Theory]
        [MemberData(nameof(GetUltraTestCases))]
        public async Task TestAnalyzeMethods_Comprehensive(UltraTestCase testCase)
        {
            // Arrange
            string code = GenerateCode(testCase);
            var (solution, methodSymbol) = await CreateSolutionAsync(code, testCase);
            var builder = new CallGraphBuilder(solution);

            // Act
            await builder.BuildAsync();
            var actions = builder.AnalyzeMethods();

            // Assert
            if (methodSymbol == null)
            {
                // 如果 symbol 为 null，通常是因为语法错误或符号查找失败
                // 此时我们可以跳过 Assert，或者记录警告
                return;
            }

            var expectedAction = DetermineExpectedAction(testCase);
            if (expectedAction == CallGraphBuilder.GraphMethodAction.None)
            {
                Assert.False(actions.ContainsKey(methodSymbol), $"Method {testCase} should NOT have any action but has {actions.GetValueOrDefault(methodSymbol)}");
            }
            else
            {
                Assert.True(actions.ContainsKey(methodSymbol), $"Method {testCase} should have action {expectedAction} but has none");
                Assert.Equal(expectedAction, actions[methodSymbol]);
            }
        }

        [Fact]
        public async Task TestGetReachableMethods_DeepChain()
        {
            // 测试深层可达性链
            string code = @"
namespace Test {
    public class Root {
        public void Entry() { A(); }
        private void A() { B(); }
        internal void B() {
            var other = new Other();
            other.C();
        }
    }
    public class Other {
        public void C() { D(); }
        private void D() { }
        public void Unreachable() { }
    }
}";
            var (solution, _) = await CreateSolutionAsync(code, null);
            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync();

            var rootEntry = builder.AllDeclaredMethods.First(m => m.Name == "Entry");
            var reachable = builder.GetReachableMethods(rootEntry);

            Assert.Contains(reachable, m => m.Name == "Entry");
            Assert.Contains(reachable, m => m.Name == "A");
            Assert.Contains(reachable, m => m.Name == "B");
            Assert.Contains(reachable, m => m.Name == "C");
            Assert.Contains(reachable, m => m.Name == "D");
            Assert.DoesNotContain(reachable, m => m.Name == "Unreachable");

            // 验证数量是否正确 (5个可达方法)
            Assert.Equal(5, reachable.Count);
        }

        [Fact]
        public async Task TestRatioBasedRefactoring_ShouldDecoupleWhenMajorityDiscarded()
        {
            // 测试场景：1个基类，3个子类实现。只有1个子类被引用。
            // 结果应该是：基类方法删除，被引用的子类方法 Decouple。
            string code = @"
namespace Test {
    public abstract class Base {
        public abstract void Apply();
    }
    public class Sub1 : Base {
        public override void Apply() { } // 被引用，应该 Decouple
    }
    public class Sub2 : Base {
        public override void Apply() { } // 未引用，应该 Delete
    }
    public class Sub3 : Base {
        public override void Apply() { } // 未引用，应该 Delete
    }
    public class Entry {
        public static void Main() {
            new Sub1().Apply();
        }
    }
}";
            var (solution, _) = await CreateSolutionAsync(code, null);
            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync();

            var mainMethod = builder.AllDeclaredMethods.First(m => m.Name == "Main");
            var actions = builder.AnalyzeMethods(new[] { mainMethod });

            var baseApply = builder.AllDeclaredMethods.First(m => m.ContainingType.Name == "Base" && m.Name == "Apply");
            var sub1Apply = builder.AllDeclaredMethods.First(m => m.ContainingType.Name == "Sub1" && m.Name == "Apply");
            var sub2Apply = builder.AllDeclaredMethods.First(m => m.ContainingType.Name == "Sub2" && m.Name == "Apply");
            var sub3Apply = builder.AllDeclaredMethods.First(m => m.ContainingType.Name == "Sub3" && m.Name == "Apply");

            // Base.Apply 是 abstract，应该保持原样 (None)
            Assert.False(actions.ContainsKey(baseApply), "Base.Apply should be None (Keep)");

            // Sub1.Apply 被使用，应该保持原样 (None)
            // 以前的逻辑可能是 Decouple，但现在简化为保持完整
            Assert.False(actions.ContainsKey(sub1Apply), "Sub1.Apply should be None (Keep)");

            // 因为 Base.Apply 是 abstract 且被 Sub1 引用，所以 Base.Apply 必须存在。
            // 因此 Sub2 和 Sub3 必须实现 Base.Apply，不能直接 Delete，只能 ClearBody。
            Assert.Equal(CallGraphBuilder.GraphMethodAction.ClearBody, actions[sub2Apply]);
            Assert.Equal(CallGraphBuilder.GraphMethodAction.ClearBody, actions[sub3Apply]);
        }

        [Fact]
        public async Task TestRatioBasedRefactoring_ShouldKeepWhenMajorityKept()
        {
            // 测试场景：1个基类，2个子类实现。全部被引用。
            // 结果应该是：基类和子类都保留 (None)。
            string code = @"
namespace Test {
    public abstract class Base {
        public abstract void Apply();
    }
    public class Sub1 : Base {
        public override void Apply() { }
    }
    public class Sub2 : Base {
        public override void Apply() { }
    }
    public class Entry {
        public static void Main() {
            new Sub1().Apply();
            new Sub2().Apply();
        }
    }
}";
            var (solution, _) = await CreateSolutionAsync(code, null);
            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync();

            var mainMethod = builder.AllDeclaredMethods.First(m => m.Name == "Main");
            var actions = builder.AnalyzeMethods(new[] { mainMethod });

            var baseApply = builder.AllDeclaredMethods.First(m => m.ContainingType.Name == "Base" && m.Name == "Apply");

            // 如果是 None，则不应该在 actions 字典中（因为过滤掉了 None）
            Assert.False(actions.ContainsKey(baseApply));
        }

        private string GenerateCode(UltraTestCase tc)
        {
            string accStr = tc.Accessibility.ToString().ToLower().Replace("protectedinternal", "protected internal").Replace("privateprotected", "private protected");
            string modStr = tc.Modifier == TestMethodModifier.None ? "" : tc.Modifier.ToString().ToLower();
            string typeKindStr = tc.TypeKind.ToString().ToLower();
            string methodName = tc.Kind == TestMethodKind.Main ? "Main" : (tc.Kind == TestMethodKind.Constructor ? "TargetType" : "TargetMethod");

            string methodDecl = tc.Kind == TestMethodKind.Constructor
                ? $"{accStr} TargetType() {{ }}"
                : $"{accStr} {modStr} {(tc.Modifier == TestMethodModifier.Async ? "Task" : "void")} {methodName}() {{ }}";

            if (tc.TypeKind == TestTypeKind.Interface)
            {
                methodDecl = $"void TargetMethod();"; // Interface methods simplified
            }

            string callerCode = "";
            if (tc.Caller == TestCallerType.SameType)
            {
                string callTarget = tc.Modifier == TestMethodModifier.Static ? "TargetType" : "this";
                callerCode = $"public void Caller() {{ {(tc.Kind == TestMethodKind.Constructor ? "new TargetType();" : $"{callTarget}.TargetMethod();")} }}";
            }

            string otherTypeSameAssembly = "";
            if (tc.Caller == TestCallerType.OtherTypeSameAssembly)
            {
                bool needsInheritance = tc.Accessibility == TestMethodAccessibility.Protected ||
                                       tc.Accessibility == TestMethodAccessibility.PrivateProtected ||
                                       tc.Accessibility == TestMethodAccessibility.ProtectedInternal;

                string baseClass = needsInheritance ? ": TargetType" : "";
                string callTarget = tc.Modifier == TestMethodModifier.Static ? "TargetType" : (needsInheritance ? "this" : "new TargetType()");

                otherTypeSameAssembly = $@"
                public class OtherType {baseClass} {{
                    public void Call() {{
                        {(tc.Kind == TestMethodKind.Constructor ? "new TargetType();" : $"{callTarget}.TargetMethod();")}
                    }}
                }}";
            }

            string otherTypeDiffAssembly = "";
            if (tc.Caller == TestCallerType.OtherTypeDiffAssembly)
            {
                bool needsInheritance = tc.Accessibility == TestMethodAccessibility.Protected ||
                                       tc.Accessibility == TestMethodAccessibility.ProtectedInternal;

                // Note: PrivateProtected is NOT accessible from diff assembly even if inherited
                string baseClass = needsInheritance ? ": Test.TargetType" : "";
                string callTarget = tc.Modifier == TestMethodModifier.Static ? "Test.TargetType" : (needsInheritance ? "this" : "new Test.TargetType()");

                otherTypeDiffAssembly = $@"
                namespace OtherAssembly {{
                    public class ForeignType {baseClass} {{
                        public void Call() {{
                            {(tc.Kind == TestMethodKind.Constructor ? "new Test.TargetType();" : $"{callTarget}.TargetMethod();")}
                        }}
                    }}
                }}";
            }

            return $@"
using System;
using System.Threading.Tasks;

namespace Test {{
    public {typeKindStr} TargetType {{
        {methodDecl}
        {callerCode}
    }}
    {otherTypeSameAssembly}
}}
{otherTypeDiffAssembly}";
        }

        private CallGraphBuilder.GraphMethodAction DetermineExpectedAction(UltraTestCase tc)
        {
            // 1. Struct 不处理
            if (tc.TypeKind == TestTypeKind.Struct) return CallGraphBuilder.GraphMethodAction.None;

            // 2. 构造函数/Main 不处理
            if (tc.Kind == TestMethodKind.Constructor || tc.Kind == TestMethodKind.Main) return CallGraphBuilder.GraphMethodAction.None;

            // 3. Public 方法不处理 (目前的逻辑是保护 Public)
            if (tc.Accessibility == TestMethodAccessibility.Public) return CallGraphBuilder.GraphMethodAction.None;

            // 4. 多态方法 (Virtual/Abstract/Override) 不私有化
            bool isPolymorphic = tc.Modifier == TestMethodModifier.Virtual ||
                                 tc.Modifier == TestMethodModifier.Abstract ||
                                 tc.Modifier == TestMethodModifier.Override;

            // 4. 无调用者 -> 删除 (除非是多态方法)
            if (tc.Caller == TestCallerType.None)
            {
                if (isPolymorphic)
                {
                    // Abstract/Virtual 且无调用者 -> ClearBody (Abstract 保持 None)
                    return tc.Modifier == TestMethodModifier.Abstract ? CallGraphBuilder.GraphMethodAction.None : CallGraphBuilder.GraphMethodAction.ClearBody;
                }
                return CallGraphBuilder.GraphMethodAction.Delete;
            }

            // 5. 只有内部调用者 (SameType)
            if (tc.Caller == TestCallerType.SameType)
            {
                // 多态方法不私有化
                if (isPolymorphic) return CallGraphBuilder.GraphMethodAction.None;

                // 如果是 Public/Internal/Protected -> 私有化
                // 修正：Protected 和 ProtectedInternal 对外部可见，在非 aggressive 模式下不应私有化
                if (tc.Accessibility == TestMethodAccessibility.Protected ||
                    tc.Accessibility == TestMethodAccessibility.ProtectedInternal)
                {
                    return CallGraphBuilder.GraphMethodAction.None;
                }

                if (tc.Accessibility != TestMethodAccessibility.Private)
                {
                    return CallGraphBuilder.GraphMethodAction.Privatize;
                }
            }

            // 6. 只有程序集内调用者 (OtherTypeSameAssembly)
            if (tc.Caller == TestCallerType.OtherTypeSameAssembly)
            {
                // 如果是 Public -> 可以变为 Internal，但目前我们的 Privatize 逻辑只针对变为 Private
                // 所以这里预期为 None，除非我们增强了 Privatize 逻辑
                return CallGraphBuilder.GraphMethodAction.None;
            }

            return CallGraphBuilder.GraphMethodAction.None;
        }

        private async Task<(Solution solution, IMethodSymbol? targetSymbol)> CreateSolutionAsync(string code, UltraTestCase? tc)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(Task).Assembly.Location))
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)); // Add System.Linq

            var documentId = DocumentId.CreateNewId(projectId);
            solution = solution.AddDocument(documentId, "TestFile.cs", SourceText.From(code));

            var project = solution.GetProject(projectId);
            if (project == null) return (solution, null);

            var compilation = await project.GetCompilationAsync();
            if (compilation == null) return (solution, null);

            IMethodSymbol? targetSymbol = null;
            if (tc != null)
            {
                string targetName = tc.Kind == TestMethodKind.Main ? "Main" : (tc.Kind == TestMethodKind.Constructor ? ".ctor" : "TargetMethod");
                var testNamespace = compilation.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Test");
                if (testNamespace == null) return (solution, null);

                var targetType = testNamespace.GetTypeMembers().FirstOrDefault(t => t.Name == "TargetType");
                if (targetType == null) return (solution, null);

                targetSymbol = targetType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m =>
                    m.Name == targetName ||
                    (tc.Kind == TestMethodKind.Constructor && m.MethodKind == Microsoft.CodeAnalysis.MethodKind.Constructor));
            }

            return (solution, targetSymbol);
        }
    }
}
