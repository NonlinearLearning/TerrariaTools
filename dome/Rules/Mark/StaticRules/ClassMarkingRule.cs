using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules
{
    /// <summary>
    /// 类标记规则。
    /// 对没有任何引用且不是其他类基类的类打上删除标记。
    /// (即：无引用且处于继承链末端/无继承关系的类可删除)。
    /// 可以进行依赖图检查因为在依赖分析阶段就包含这些信息了!!
    /// </summary>
    public class ClassMarkingRule
    {
        public string Name => "类标记规则";

        /// <summary>
        /// 异步分析并标记类声明。
        /// </summary>
        public async Task<ClassDeclarationSyntax> MarkClassAsync(ClassDeclarationSyntax classDeclaration, SemanticModel model, Solution solution)
        {
            var symbol = model.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
            if (symbol == null) return classDeclaration;

            // 1. 检查引用情况
            // 如果有外部引用，则保留
            bool hasReferences = await ReferenceAnalyzer.HasReferencesAsync(symbol, solution);
            if (hasReferences)
            {
                return classDeclaration;
            }

            // 2. 检查继承链依赖 (是否作为基类被其他类继承)
            // 如果有派生类，说明它是继承链的一部分（父类），需要保留
            bool hasDerivedClasses = await InheritanceAnalyzer.HasDerivedClassesAsync(symbol, solution);
            if (hasDerivedClasses)
            {
                return classDeclaration;
            }

            // 3. 既无引用也无派生类 -> 标记删除
            // 这包括：独立的孤儿类，或者继承链中无引用的末端子类
            var deleteAnnotation = new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, RuleConstants.ActionDelete);
            return classDeclaration.WithAdditionalAnnotations(deleteAnnotation);
        }
    }

    /// <summary>
    /// 类标记规则演示。
    /// </summary>
    public static class ClassMarkingDemo
    {
        public static async Task RunAsync()
        {
            Console.WriteLine("=== 类标记规则演示 (引用与继承依赖分析) ===");

            // 1. 构建示例环境
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                .WithMetadataReferences(new[] {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
                });

            // 代码结构：
            // Base (无引用, 有派生 Middle) -> 保留 (父类)
            // Middle (无引用, 有派生 Leaf) -> 保留 (父类)
            // Leaf (无引用, 无派生) -> 删除 (继承链末端)
            // UsedClass (有引用) -> 保留
            // Standalone (无引用, 无派生) -> 删除
            string code = @"
using System;

// 继承链: Base -> Middle -> Leaf
public class BaseClass { }
public class MiddleClass : BaseClass { }
public class LeafClass : MiddleClass { }

// 被引用的类
public class UsedClass { }

// 独立的类
public class StandaloneClass { }

// 引用者
public class Client {
    void Method() {
        var u = new UsedClass();
    }
}
";
            var document = workspace.AddDocument(project.Id, "Code.cs", SourceText.From(code));

            // 获取最新编译状态
            var solution = document.Project.Solution;
            var currentProject = solution.GetProject(project.Id);
            var model = await currentProject.Documents.First().GetSemanticModelAsync();
            var root = await model.SyntaxTree.GetRootAsync();

            var rule = new ClassMarkingRule();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            Console.WriteLine("\n[分析结果]:");
            foreach (var classDecl in classes)
            {
                string className = classDecl.Identifier.Text;
                if (className == "Client") continue; // 跳过测试辅助类

                var markedClass = await rule.MarkClassAsync(classDecl, model, solution);
                var annotation = markedClass.GetAnnotations(RuleConstants.RewriteAnnotationKind).FirstOrDefault();

                Console.Write($"类: {className,-15}");

                if (annotation != null)
                {
                    Console.WriteLine($" -> 【标记删除】 ({annotation.Data})");
                }
                else
                {
                    Console.WriteLine($" -> 【保留】 (有引用或有派生类)");
                }
            }
        }
    }
}
