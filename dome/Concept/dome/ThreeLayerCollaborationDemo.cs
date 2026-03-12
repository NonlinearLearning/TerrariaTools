using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 演示 Roslyn 三层架构（Syntax, Semantic/Symbol, Operation）的深度协作。
    /// 包含 5 个复杂的跨层级分析案例。
    /// </summary>
    public class ThreeLayerCollaborationDemo
    {
        public static void Run()
        {
            RunDemo();
        }

        public static void RunDemo()
        {
            // 0. 准备复杂的待分析代码
            string code = @"
using System;
namespace Example
{
    public class DataProcessor
    {
        private int _globalCounter = 0;

        public void ProcessData(string input, int factor)
        {
            // Case 1: 变量声明与修改追踪
            int result = 10;
            if (input.Length > 5)
            {
                result = result + factor; // 修改
            }

            // Case 2: 方法调用与参数流向
            Console.WriteLine(result);
            DangerousMethod(input); // 敏感调用

            // Case 3: 隐式类型转换风险
            double d = result;
            int i = (int)(d * 1.5); // 显式转换

            // Case 4: 未使用的变量
            int unused = 100;

            // Case 5: 字段修改副作用
            _globalCounter++;
        }

        private void DangerousMethod(string query) { }
    }
}";

            // 1. 构建编译环境
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("ThreeLayerDemo")
                                               .AddReferences(mscorlib)
                                               .AddSyntaxTrees(tree);
            SemanticModel model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            Console.WriteLine("=== Roslyn 三层协作深度分析演示 ===\n");

            // 获取方法节点作为分析入口
            var methodNode = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "ProcessData");
            var methodSymbol = model.GetDeclaredSymbol(methodNode);
            var methodBodyOp = model.GetOperation(methodNode.Body) as IBlockOperation;

            if (methodBodyOp == null) return;

            // 执行 5 个协作分析案例
            AnalyzeVariableModifications(model, methodBodyOp);
            AnalyzeMethodCallChain(model, methodBodyOp);
            AnalyzeTypeConversions(model, methodBodyOp);
            AnalyzeUnusedVariables(model, methodNode);
            AnalyzeFieldSideEffects(model, methodBodyOp);
        }

        /// <summary>
        /// 案例 1: 变量修改追踪
        /// 协作模式: Operation (识别赋值) -> Symbol (识别变量身份) -> Syntax (定位源码位置)
        /// </summary>
        private static void AnalyzeVariableModifications(SemanticModel model, IBlockOperation body)
        {
            Console.WriteLine("--- 案例 1: 变量修改追踪 ---");
            // 遍历所有操作，寻找赋值操作 (ISimpleAssignmentOperation)
            foreach (var op in body.Descendants().OfType<ISimpleAssignmentOperation>())
            {
                // Operation 层: 识别这是一个赋值行为 (Target = Value)
                var target = op.Target;

                // Operation -> Symbol 层: 获取被赋值的目标是谁
                // 注意：这里需要处理可能的成员访问或数组访问，简单起见我们只处理局部变量引用
                if (target is ILocalReferenceOperation localRef)
                {
                    var symbol = localRef.Local; // Symbol 层: 拿到 ILocalSymbol

                    // Symbol -> Syntax 层: 获取该变量声明的位置
                    var declSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

                    Console.WriteLine($"[发现修改] 变量 '{symbol.Name}' 被修改了。");
                    Console.WriteLine($"  - 赋值语句位置: 行 {op.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
                    Console.WriteLine($"  - 变量定义位置: 行 {declSyntax?.GetLocation().GetLineSpan().StartLinePosition.Line + 1 ?? -1}");

                    // 进一步分析: 赋值的源头是什么？
                    if (op.Value is IBinaryOperation binaryOp)
                    {
                        Console.WriteLine($"  - 赋值源逻辑: 这是一个二元运算 ({binaryOp.OperatorKind})");
                    }
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 案例 2: 敏感 API 调用参数追踪
        /// 协作模式: Operation (识别调用) -> Symbol (确认方法身份) -> Operation (递归分析参数来源)
        /// </summary>
        private static void AnalyzeMethodCallChain(SemanticModel model, IBlockOperation body)
        {
            Console.WriteLine("--- 案例 2: 敏感 API 调用参数追踪 ---");
            var invocations = body.Descendants().OfType<IInvocationOperation>();

            foreach (var invoke in invocations)
            {
                // Symbol 层: 检查调用的目标方法名
                var targetMethod = invoke.TargetMethod;
                if (targetMethod.Name == "DangerousMethod")
                {
                    Console.WriteLine($"[警报] 发现了对敏感方法 '{targetMethod.Name}' 的调用");

                    // Operation 层: 分析传递给它的参数
                    foreach (var arg in invoke.Arguments)
                    {
                        Console.WriteLine($"  - 参数 '{arg.Parameter.Name}' 的值来源:");
                        TraceValueOrigin(arg.Value);
                    }
                }
            }
            Console.WriteLine();
        }

        private static void TraceValueOrigin(IOperation valueOp)
        {
            if (valueOp is IParameterReferenceOperation paramRef)
            {
                Console.WriteLine($"    -> 直接来自当前方法的参数: {paramRef.Parameter.Name} (危险传递!)");
            }
            else if (valueOp is ILocalReferenceOperation localRef)
            {
                Console.WriteLine($"    -> 来自局部变量: {localRef.Local.Name}");
            }
            // 可以递归继续追踪变量的定义...
        }

        /// <summary>
        /// 案例 3: 隐式类型转换安全检查
        /// 协作模式: Operation (识别转换) -> Symbol (获取类型信息) -> Semantic (判断类型兼容性)
        /// </summary>
        private static void AnalyzeTypeConversions(SemanticModel model, IBlockOperation body)
        {
            Console.WriteLine("--- 案例 3: 类型转换安全检查 ---");
            var conversions = body.Descendants().OfType<IConversionOperation>();

            foreach (var conv in conversions)
            {
                // Operation 层: 区分隐式/显式转换
                if (conv.IsImplicit)
                {
                    // Symbol 层: 获取源类型和目标类型
                    var sourceType = conv.Operand.Type;
                    var targetType = conv.Type;

                    if (sourceType != null && targetType != null && sourceType.Name != targetType.Name)
                    {
                        Console.WriteLine($"[隐式转换] 从 {sourceType.Name} -> {targetType.Name}");
                        Console.WriteLine($"  - 代码: {conv.Syntax.ToString().Trim()}");

                        // 简单判断: 可能会丢失精度吗？(例如 int -> double 通常安全，但 long -> int 不安全)
                        if (targetType.SpecialType == SpecialType.System_Double && sourceType.SpecialType == SpecialType.System_Int32)
                        {
                             Console.WriteLine("  - 状态: 安全 (宽化转换)");
                        }
                    }
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 案例 4: 未使用变量检测
        /// 协作模式: Syntax (定义域) -> DataFlow (控制流分析) -> Symbol (变量集合)
        /// </summary>
        private static void AnalyzeUnusedVariables(SemanticModel model, MethodDeclarationSyntax methodNode)
        {
            Console.WriteLine("--- 案例 4: 未使用变量检测 ---");

            // DataFlow 层: 分析整个方法体的数据流
            // 注意: AnalyzeDataFlow 是基于 Syntax 节点的 API，但返回的是 Symbol 信息
            var dataFlow = model.AnalyzeDataFlow(methodNode.Body);

            // Syntax 层: 找到所有的变量声明
            var allDeclarations = methodNode.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>();

            foreach (var decl in allDeclarations)
            {
                // Semantic 层: 获取声明对应的符号
                var symbol = model.GetDeclaredSymbol(decl);
                if (symbol == null) continue;

                // 核心逻辑: 如果一个变量在 AlwaysAssigned 集合中（或者被声明了），但不在 ReadInside 集合中
                // DataFlow.ReadInside: 在该区域内被读取的变量
                if (!dataFlow.ReadInside.Contains(symbol))
                {
                    Console.WriteLine($"[冗余代码] 变量 '{symbol.Name}' 被声明但这方法内从未被读取。");
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 案例 5: 字段修改副作用分析
        /// 协作模式: Operation (一元运算) -> Symbol (字段识别) -> Semantic (作用域检查)
        /// </summary>
        private static void AnalyzeFieldSideEffects(SemanticModel model, IBlockOperation body)
        {
            Console.WriteLine("--- 案例 5: 字段修改副作用分析 ---");

            // 查找所有的一元递增/递减操作 (如 i++, --j)
            var increments = body.Descendants().OfType<IIncrementOrDecrementOperation>();

            foreach (var op in increments)
            {
                // Operation 层: 获取操作的目标
                if (op.Target is IFieldReferenceOperation fieldRef)
                {
                    // Symbol 层: 拿到字段符号
                    var field = fieldRef.Field;

                    // Semantic 层: 判断这个字段的作用域
                    string scope = field.ContainingType.Name;
                    Console.WriteLine($"[副作用] 方法修改了外部字段: {scope}.{field.Name}");
                    Console.WriteLine($"  - 操作类型: {(op.Kind == OperationKind.Increment ? "自增" : "自减")}");
                    Console.WriteLine($"  - 警告: 这可能导致线程安全问题或隐式状态依赖。");
                }
            }
            Console.WriteLine();
        }
    }
}
