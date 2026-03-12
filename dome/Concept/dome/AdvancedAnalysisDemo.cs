using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 高级综合分析演示 (Advanced Analysis Demo)
    /// 实现 5 个具体案例：
    /// 1. 变量修改追踪
    /// 2. 方法调用链分析
    /// 3. 类型转换安全检查
    /// 4. 未使用变量检测
    /// 5. 敏感 API 参数追踪
    /// </summary>
    public static class AdvancedAnalysisDemo
    {
        public static async Task RunAsync()
        {
            Console.WriteLine("=== 启动 AdvancedAnalysis Demo (5个案例) ===");

            // 案例 1: 变量修改追踪
            Console.WriteLine("\n>>> 案例 1: 变量修改追踪 (Variable Modification Tracking)");
            RunVariableModificationTracking();

            // 案例 2: 方法调用链分析 (需配合 SymbolFinder)
            Console.WriteLine("\n>>> 案例 2: 方法调用链分析 (Method Call Chain)");
            await RunMethodCallChainAnalysisAsync();

            // 案例 3: 类型转换安全检查
            Console.WriteLine("\n>>> 案例 3: 类型转换安全检查 (Type Conversion Safety)");
            RunTypeConversionCheck();

            // 案例 4: 未使用变量检测 (DataFlow)
            Console.WriteLine("\n>>> 案例 4: 未使用变量检测 (Unused Variable Detection)");
            RunUnusedVariableDetection();

            // 案例 5: 敏感 API 参数追踪
            Console.WriteLine("\n>>> 案例 5: 敏感 API 参数追踪 (Sensitive API Tracking)");
            RunSensitiveApiTracking();

            Console.WriteLine("\n=== AdvancedAnalysis 演示结束 ===");
        }

        private static void RunVariableModificationTracking()
        {
            string code = @"
class C {
    void M() {
        int x = 0;
        x = 1;      // 修改
        x++;        // 修改
        int y = x;  // 读取
    }
}";
            var (tree, model) = CreateModel(code);
            var root = tree.GetRoot();
            var variableDeclarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().First(v => v.Identifier.Text == "x");
            var symbol = model.GetDeclaredSymbol(variableDeclarator);

            Console.WriteLine($"分析变量: {symbol.Name}");
            var references = root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(id => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id).Symbol, symbol));

            foreach (var refNode in references)
            {
                var dataFlow = model.AnalyzeDataFlow(refNode);
                // 简单的读写判断：检查父节点类型
                var parent = refNode.Parent;
                bool isWritten = false;
                
                if (parent is AssignmentExpressionSyntax assign && assign.Left == refNode) isWritten = true;
                else if (parent is PostfixUnaryExpressionSyntax) isWritten = true;
                else if (parent is PrefixUnaryExpressionSyntax) isWritten = true;

                if (isWritten)
                {
                    Console.WriteLine($"  - 修改点: 行 {refNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1} ({refNode.Parent})");
                }
            }
        }

        private static async Task RunMethodCallChainAnalysisAsync()
        {
            // 模拟多层调用: A -> B -> C
            string code = @"
class Service {
    public void MethodC() {}
    public void MethodB() { MethodC(); }
    public void MethodA() { MethodB(); }
}";
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("DemoProject", LanguageNames.CSharp)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddDocument("Service.cs", code).Project;
            
            var compilation = await project.GetCompilationAsync();
            var methodC = compilation.GetTypeByMetadataName("Service").GetMembers("MethodC").First();

            Console.WriteLine($"目标方法: {methodC.Name}");
            // 查找谁调用了 C
            var callers = await SymbolFinder.FindCallersAsync(methodC, project.Solution);
            foreach (var caller in callers)
            {
                Console.WriteLine($"  <- 被调用者: {caller.CallingSymbol.Name}");
                // 递归查找可以构建完整链条
            }
        }

        private static void RunTypeConversionCheck()
        {
            string code = @"
class C {
    void M() {
        long l = 100;
        int i = (int)l; // 显式转换，可能有风险
        object o = ""test"";
        string s = (string)o; // 显式引用转换
    }
}";
            var (tree, model) = CreateModel(code);
            var castExpressions = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>();

            foreach (var cast in castExpressions)
            {
                var typeInfo = model.GetTypeInfo(cast.Type);
                var expressionTypeInfo = model.GetTypeInfo(cast.Expression);
                
                Console.WriteLine($"转换检查: 行 {cast.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
                Console.WriteLine($"  - 从 {expressionTypeInfo.Type?.Name} 到 {typeInfo.Type?.Name}");
                
                var conversion = model.ClassifyConversion(cast.Expression, typeInfo.Type);
                Console.WriteLine($"  - 转换类型: {(conversion.IsImplicit ? "Implicit" : "Explicit")}, {(conversion.IsNumeric ? "Numeric" : "Reference")}");
                if (conversion.IsNumeric && !conversion.IsImplicit)
                {
                    Console.WriteLine("  [警告] 显式数值转换，可能存在溢出风险。");
                }
            }
        }

        private static void RunUnusedVariableDetection()
        {
             string code = @"
class C {
    void M() {
        int used = 1;
        int unused = 2;
        Console.WriteLine(used);
    }
}";
            var (tree, model) = CreateModel(code);
            var root = tree.GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var dataFlow = model.AnalyzeDataFlow(method.Body);

            Console.WriteLine("变量状态:");
            foreach (var variable in dataFlow.VariablesDeclared)
            {
                bool isRead = dataFlow.ReadInside.Contains(variable);
                Console.WriteLine($"  - {variable.Name}: {(isRead ? "已使用" : "[未使用]")}");
            }
        }

        private static void RunSensitiveApiTracking()
        {
            string code = @"
class C {
    void M(string password) {
        Log(password); // 敏感数据流入 Log
    }
    void Log(string msg) {}
}";
            var (tree, model) = CreateModel(code);
            var root = tree.GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "M");
            
            // 简单污点分析模拟：查找参数 password 的流向
            var paramSymbol = model.GetDeclaredSymbol(method.ParameterList.Parameters[0]);
            Console.WriteLine($"追踪敏感参数: {paramSymbol.Name}");

            var invocations = method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                foreach (var arg in invocation.ArgumentList.Arguments)
                {
                    var argSymbol = model.GetSymbolInfo(arg.Expression).Symbol;
                    if (SymbolEqualityComparer.Default.Equals(argSymbol, paramSymbol))
                    {
                        var targetMethod = model.GetSymbolInfo(invocation).Symbol;
                        Console.WriteLine($"  [警报] 敏感数据传递给方法: {targetMethod.Name} 在行 {invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
                    }
                }
            }
        }

        private static (SyntaxTree, SemanticModel) CreateModel(string code)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(code);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("Demo")
                .AddReferences(mscorlib)
                .AddSyntaxTrees(tree);
            return (tree, compilation.GetSemanticModel(tree));
        }
    }
}
