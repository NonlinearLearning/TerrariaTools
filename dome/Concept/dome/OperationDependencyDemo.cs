using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 演示如何使用 Roslyn 的 IOperation 树进行高级语义分析。
    /// IOperation 提供了一个跨语言（C# 和 VB.NET）的抽象操作层。
    /// </summary>
    public class OperationDependencyDemo
    {
        /// <summary>
        /// 运行 IOperation 树分析演示。
        /// </summary>
        public static void Run()
        {
            RunDemo();
        }

        /// <summary>
        /// 执行具体的演示逻辑。
        /// </summary>
        public static void RunDemo()
        {
            // 1. 准备待分析的源代码
            string code = @"
using System;

namespace Example
{
    public class OperationLevelDemo
    {
        public void Process(int input)
        {
            int x = input + 5;           // 局部变量声明
            Console.WriteLine(x * 2);    // 方法调用 + 算术运算
        }
    }
}";

            // 2. 创建编译对象并获取语义模型
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("OperationDemo")
                                               .AddReferences(mscorlib)
                                               .AddSyntaxTrees(tree);
            SemanticModel model = compilation.GetSemanticModel(tree);

            // 3. 查找目标方法
            var methodNode = tree.GetRoot().DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>()
                                 .FirstOrDefault(m => m.Identifier.Text == "Process");

            if (methodNode == null || methodNode.Body == null) return;

            Console.WriteLine($"=== 分析方法: {methodNode.Identifier.Text} 的 IOperation 树 ===\n");

            // 4. 获取方法体的根操作 (IBlockOperation)
            IOperation? rootOperation = model.GetOperation(methodNode.Body);
            if (rootOperation is IBlockOperation blockOperation)
            {
                int step = 1;
                foreach (var operation in blockOperation.Operations)
                {
                    Console.WriteLine($"步骤 {step++}: 分析操作类型 [{operation.Kind}]");
                    AnalyzeOperation(operation);
                    Console.WriteLine();
                }
            }

            Console.WriteLine("=== 分析结束 ===");
        }

        /// <summary>
        /// 递归或分类分析不同的 IOperation 类型。
        /// </summary>
        /// <param name="operation">待分析的操作节点。</param>
        private static void AnalyzeOperation(IOperation operation)
        {
            switch (operation)
            {
                // 1. 处理局部变量声明 (例如: int x = input + 5;)
                case IVariableDeclarationGroupOperation varDeclGroup:
                    foreach (var decl in varDeclGroup.Declarations)
                    {
                        foreach (var declarator in decl.Declarators)
                        {
                            Console.WriteLine($"  -> 声明变量: {declarator.Symbol.Name} (类型: {declarator.Symbol.Type})");
                            if (declarator.Initializer?.Value != null)
                            {
                                Console.WriteLine("     - 初始化表达式依赖于:");
                                FindReferences(declarator.Initializer.Value);
                            }
                        }
                    }
                    break;

                // 2. 处理表达式语句 (例如: Console.WriteLine(x * 2);)
                case IExpressionStatementOperation exprStmt:
                    Console.WriteLine("  -> 表达式语句执行内容:");
                    AnalyzeExpression(exprStmt.Operation);
                    break;

                default:
                    Console.WriteLine($"  -> 其他操作类型: {operation.GetType().Name}");
                    break;
            }
        }

        /// <summary>
        /// 分析表达式中的具体操作。
        /// </summary>
        /// <param name="expression">表达式操作。</param>
        private static void AnalyzeExpression(IOperation expression)
        {
            if (expression is IInvocationOperation invocation)
            {
                var method = invocation.TargetMethod;
                Console.WriteLine($"     - 调用方法: {method.ContainingType.Name}.{method.Name}");
                
                // 检查参数依赖
                foreach (var arg in invocation.Arguments)
                {
                    Console.WriteLine("       - 参数依赖于:");
                    FindReferences(arg.Value);
                }
            }
        }

        /// <summary>
        /// 在操作树中寻找所有的引用依赖（如变量引用、参数引用）。
        /// </summary>
        /// <param name="root">起始操作节点。</param>
        private static void FindReferences(IOperation root)
        {
            // Descendants() 会返回当前节点下的所有子节点
            var references = root.DescendantsAndSelf();

            foreach (var op in references)
            {
                if (op is ILocalReferenceOperation localRef)
                {
                    Console.WriteLine($"       * 引用了局部变量: {localRef.Local.Name}");
                }
                else if (op is IParameterReferenceOperation paramRef)
                {
                    Console.WriteLine($"       * 引用了函数参数: {paramRef.Parameter.Name}");
                }
                else if (op is IBinaryOperation binaryOp)
                {
                    Console.WriteLine($"       * 包含算术运算: {binaryOp.OperatorKind}");
                }
            }
        }
    }
}
