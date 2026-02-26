using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis;
using Xunit;
using Xunit.Abstractions;
using static TerrariaTools.Analysis.CallGraphBuilder;

namespace TerrariaTools.UnitTests.DependencyGraphTests
{
    public class CallGraphBuilderComplexTests
    {
        private readonly ITestOutputHelper _output;

        public CallGraphBuilderComplexTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public class MethodTestCase
        {
            public string TypeKind { get; set; } = "class";
            public string MethodName { get; set; } = "Other";
            public string Accessibility { get; set; } = "public";
            public bool IsStatic { get; set; }
            public bool IsAbstract { get; set; }
            public bool IsVirtual { get; set; }
            public bool IsOverride { get; set; }
            public bool IsInterfaceImpl { get; set; }
            public int CallerCount { get; set; }
            public string CallerLocation { get; set; } = "SameType"; // "SameType", "OtherType"

            public override string ToString()
            {
                return $"{Accessibility} {Modifiers} {MethodName} (Type: {TypeKind}, Interface: {IsInterfaceImpl}, Callers: {CallerCount} from {CallerLocation})";
            }

            public string Modifiers
            {
                get
                {
                    var mods = new List<string>();
                    if (IsStatic) mods.Add("static");
                    if (IsAbstract) mods.Add("abstract");
                    if (IsVirtual) mods.Add("virtual");
                    if (IsOverride) mods.Add("override");
                    return string.Join(" ", mods);
                }
            }
        }

        public static IEnumerable<object[]> GetTestCases()
        {
            var typeKinds = new[] { "class", "struct" };
            var methodNames = new[] { "Main", "Other" };
            var accessibilities = new[] { "public", "internal", "protected", "private" };
            var isStatics = new[] { true, false };
            var isAbstracts = new[] { true, false };
            var isVirtuals = new[] { true, false };
            var isOverrides = new[] { true, false };
            var isInterfaceImpls = new[] { true, false };
            var callerCounts = new[] { 0, 1, 2 }; 
            var callerLocations = new[] { "SameType", "OtherType" };

            foreach (var typeKind in typeKinds)
            {
                foreach (var methodName in methodNames)
                {
                    foreach (var acc in accessibilities)
                    {
                        foreach (var isStatic in isStatics)
                        {
                            if (methodName == "Main" && !isStatic) continue; 
                            if (typeKind == "struct" && (isAbstracts.Any(x => x) || isVirtuals.Any(x => x) || isOverrides.Any(x => x))) continue;

                            foreach (var isAbstract in isAbstracts)
                            {
                                if (isAbstract && isStatic) continue;
                                if (isAbstract && typeKind == "struct") continue;

                                foreach (var isVirtual in isVirtuals)
                                {
                                    if (isVirtual && isStatic) continue;
                                    if (isVirtual && isAbstract) continue;

                                    foreach (var isOverride in isOverrides)
                                    {
                                        if (isOverride && isStatic) continue;
                                        if (isOverride && (isAbstract || isVirtual)) continue;
                                        if (isOverride && methodName == "Main") continue; 
                                        if (isOverride && acc != "public") continue; // Base is public virtual, so override must be public

                                        foreach (var isInterface in isInterfaceImpls)
                                        {
                                            if (isInterface && isStatic) continue;
                                            if (isInterface && acc != "public") continue;
                                            if (isInterface && isOverride) continue;
                                            if (isInterface && methodName == "Main") continue; 

                                            foreach (var count in callerCounts)
                                            {
                                                foreach (var loc in callerLocations)
                                                {
                                                    if (count == 0 && loc == "OtherType") continue;
                                                    if (loc == "OtherType" && (acc == "private" || acc == "protected")) continue; // Cannot access private/protected from unrelated class

                                                    yield return new object[] {
                                                        new MethodTestCase {
                                                            TypeKind = typeKind,
                                                            MethodName = methodName,
                                                            Accessibility = acc,
                                                            IsStatic = isStatic,
                                                            IsAbstract = isAbstract,
                                                            IsVirtual = isVirtual,
                                                            IsOverride = isOverride,
                                                            IsInterfaceImpl = isInterface,
                                                            CallerCount = count,
                                                            CallerLocation = loc
                                                        }
                                                    };
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(GetTestCases))]
        public async Task AnalyzeMethods_ComplexScenarios(MethodTestCase testCase)
        {
            // 1. Generate Source Code
            var source = GenerateSource(testCase);
            
            // 2. Setup Workspace
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject.dll", LanguageNames.CSharp)
                .WithMetadataReferences(new[] { 
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
                });
            var project = workspace.AddProject(projectInfo);
            var doc = workspace.AddDocument(project.Id, "Program.cs", SourceText.From(source));

            workspace.TryApplyChanges(workspace.CurrentSolution);
            var solution = workspace.CurrentSolution;

            // 3. Build Call Graph
            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync();
            var results = builder.AnalyzeMethods();

            // 4. Determine Expected Result (Oracle)
            var expectedAction = DetermineExpectedAction(testCase);

            // 5. Assert
            var compilation = await solution.Projects.First().GetCompilationAsync();
            if (compilation == null) throw new Exception("Compilation failed");
            
            var type = compilation.GetTypeByMetadataName($"TestProject.Target{testCase.TypeKind}");
            if (type == null) throw new Exception($"Could not find type TestProject.Target{testCase.TypeKind} in source:\n{source}");

            var methodSymbol = type.GetMembers(testCase.MethodName).OfType<IMethodSymbol>().First();

            if (expectedAction == GraphMethodAction.None)
            {
                Assert.False(results.ContainsKey(methodSymbol), 
                    $"Method {testCase} should NOT be modified. Found: {results.GetValueOrDefault(methodSymbol)}.\nSource:\n{source}");
            }
            else
            {
                Assert.True(results.ContainsKey(methodSymbol), 
                    $"Method {testCase} SHOULD be {expectedAction}. Not found in results.\nSource:\n{source}");
                Assert.Equal(expectedAction, results[methodSymbol]);
            }
        }

        private string GenerateSource(MethodTestCase testCase)
        {
            var code = "using System;\nnamespace TestProject {\n";

            if (testCase.IsInterfaceImpl)
            {
                code += "    public interface ITest { void Other(); }\n";
            }

            if (testCase.IsOverride)
            {
                code += "    public class Base { public virtual void Other() {} }\n";
            }

            var inheritance = "";
            var interfaces = new List<string>();
            if (testCase.IsOverride) interfaces.Add("Base");
            if (testCase.IsInterfaceImpl) interfaces.Add("ITest");
            
            if (interfaces.Any()) inheritance = " : " + string.Join(", ", interfaces);

            code += $"    public {testCase.TypeKind} Target{testCase.TypeKind} {inheritance} {{\n";
            
            var methodBody = testCase.IsAbstract ? ";" : "{ }";
            code += $"        {testCase.Accessibility} {testCase.Modifiers} void {testCase.MethodName}() {methodBody}\n";

            // Callers
            if (testCase.CallerCount > 0 && testCase.CallerLocation == "SameType")
            {
                code += "        public void Caller() { " + (testCase.IsStatic ? "" : "this.") + testCase.MethodName + "(); }\n";
                if (testCase.CallerCount > 1)
                     code += "        public void Caller2() { " + (testCase.IsStatic ? "" : "this.") + testCase.MethodName + "(); }\n";
            }

            code += "    }\n"; // End Type

            if (testCase.CallerCount > 0 && testCase.CallerLocation == "OtherType")
            {
                code += "    public class OtherClass {\n";
                code += "        public void Caller() {\n";
                if (testCase.IsStatic)
                    code += $"            Target{testCase.TypeKind}.{testCase.MethodName}();\n";
                else
                    code += $"            new Target{testCase.TypeKind}().{testCase.MethodName}();\n";
                code += "        }\n";
                if (testCase.CallerCount > 1)
                {
                    code += "        public void Caller2() {\n";
                    if (testCase.IsStatic)
                        code += $"            Target{testCase.TypeKind}.{testCase.MethodName}();\n";
                    else
                        code += $"            new Target{testCase.TypeKind}().{testCase.MethodName}();\n";
                    code += "        }\n";
                }
                code += "    }\n";
            }

            code += "}\n";
            return code;
        }

        private GraphMethodAction DetermineExpectedAction(MethodTestCase testCase)
        {
            // Phase 1: DetermineAction (Only if Callers == 0)
            if (testCase.CallerCount == 0)
            {
                var action = DetermineAction(testCase);
                // If None, Builder stops here (because 0 callers -> not in reverse graph)
                return action; 
            }
            
            // Phase 2: ShouldPrivatize (Only if Callers > 0)
            // If CallerCount > 0, Phase 1 skipped (implicitly None).
            
            if (ShouldPrivatize(testCase))
            {
                return GraphMethodAction.Privatize;
            }

            return GraphMethodAction.None;
        }

        private GraphMethodAction DetermineAction(MethodTestCase testCase)
        {
            if (testCase.TypeKind == "struct") return GraphMethodAction.None;
            if (testCase.MethodName == "Main" && testCase.IsStatic) return GraphMethodAction.None;
            
            // CallGraphBuilder.cs logic: Public check comes BEFORE Interface check
            if (testCase.Accessibility == "public") return GraphMethodAction.None;

            if (testCase.IsInterfaceImpl || testCase.IsAbstract || testCase.IsVirtual || testCase.IsOverride)
            {
                if (testCase.IsAbstract) return GraphMethodAction.None;
                return GraphMethodAction.ClearBody;
            }

            return GraphMethodAction.Delete;
        }

        private bool ShouldPrivatize(MethodTestCase testCase)
        {
            if (testCase.Accessibility != "public" && testCase.Accessibility != "internal") return false;
            
            // CallGraphBuilder.cs logic: IsVisibleToExternal check
            // Public methods are visible externally, so not privatized in non-aggressive mode
            if (testCase.Accessibility == "public") return false;

            if (testCase.IsInterfaceImpl) return false;
            if (testCase.IsAbstract || testCase.IsVirtual || testCase.IsOverride) return false;
            if (testCase.MethodName == "Main" && testCase.IsStatic) return false;

            if (testCase.CallerCount > 0)
            {
                if (testCase.CallerLocation == "OtherType") return false;
            }
            
            return true;
        }
    }
}
