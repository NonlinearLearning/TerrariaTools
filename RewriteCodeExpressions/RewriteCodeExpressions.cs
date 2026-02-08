/**
 * 功能描述：提供基于 Roslyn 的 C# 表达式简化和重写逻辑，支持根据特定规则移除或替换语法节点。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 表达式处理工具类
    /// </summary>
    public static class ExpressionProcessor
    {
        /// <summary>
        /// 从表达式中移除指定的子表达式。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="TargetToRemove">要移除的目标节点</param>
        /// <param name="Model">语义模型</param>
        /// <returns>移除后的语法节点</returns>
        public static SyntaxNode? RemoveExpressionPart(SyntaxNode Root, SyntaxNode TargetToRemove, SemanticModel? Model = null)
        {
            if (Root == null || TargetToRemove == null) return Root;
            return RemoveParts(Root, Node => Node == TargetToRemove, Model);
        }

        /// <summary>
        /// 移除带有指定类型标注的语法节点。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="AnnotationKind">标注类型名称</param>
        /// <param name="Model">语义模型</param>
        /// <returns>移除后的语法节点</returns>
        public static SyntaxNode? RemoveAnnotatedParts(SyntaxNode Root, string AnnotationKind, SemanticModel? Model = null)
        {
            if (Root == null || string.IsNullOrEmpty(AnnotationKind)) return Root;
            return RemoveParts(Root, Node => Node.GetAnnotations(AnnotationKind).Any(), Model);
        }

        /// <summary>
        /// 移除带有指定标注对象的语法节点。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="Annotation">标注对象</param>
        /// <param name="Model">语义模型</param>
        /// <returns>移除后的语法节点</returns>
        public static SyntaxNode? RemoveAnnotatedParts(SyntaxNode Root, SyntaxAnnotation Annotation, SemanticModel? Model = null)
        {
            if (Root == null || Annotation == null) return Root;
            return RemoveParts(Root, Node => Node.HasAnnotation(Annotation), Model);
        }

        /// <summary>
        /// 查找语法树中带有指定批注（Annotation）的节点，并执行移除。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="AnnotationKind">批注类型（Kind）</param>
        /// <param name="Model">可选的语义模型</param>
        /// <returns>处理后的新语法树根节点</returns>
        public static SyntaxNode ProcessAnnotatedNodes(SyntaxNode Root, string AnnotationKind, SemanticModel? Model = null)
        {
            if (Root == null || string.IsNullOrEmpty(AnnotationKind)) return Root!;
            return RemoveAnnotatedParts(Root, AnnotationKind, Model)!;
        }

        /// <summary>
        /// 查找语法树中具有特定批注对象的节点，并将其移除。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="Annotation">指定的批注对象</param>
        /// <param name="Model">可选的语义模型</param>
        /// <returns>处理后的新语法树根节点</returns>
        public static SyntaxNode ProcessAnnotatedNodes(SyntaxNode Root, SyntaxAnnotation Annotation, SemanticModel? Model = null)
        {
            if (Root == null || Annotation == null) return Root!;
            return RemoveAnnotatedParts(Root, Annotation, Model)!;
        }

        /// <summary>
        /// 根据自定义谓词移除符合条件的语法节点。
        /// 该方法采用"两阶段"处理模式：
        /// 1. 标记阶段：通过 CollectNodesToMark 收集所有直接匹配谓词及其语义关联的节点。
        /// 2. 重写阶段：使用 ExpressionSimplifier 执行实际的节点移除、简化或占位符替换。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="ShouldRemove">判断是否需要移除节点的"谓词"委托。通常用于检查节点是否带有特定标记。</param>
        /// <param name="Model">语义模型，用于分析变量引用和数据流传播</param>
        /// <returns>处理后的语法节点</returns>
        public static SyntaxNode? RemoveParts(SyntaxNode Root, System.Func<SyntaxNode, bool> ShouldRemove, SemanticModel? Model = null)
        {
            if (Root == null || ShouldRemove == null) return Root;

            // 定义用于标识待移除节点的内部标注
            var ToRemoveAnnotation = new SyntaxAnnotation("CleanTools_ToRemove");

            // 第一阶段：收集所有需要标记移除的节点集合
            var NodesToMark = CollectNodesToMark(Root, ShouldRemove, Model);
            if (NodesToMark.Count == 0) return Root;

            // 为收集到的节点统一打上"待移除"标注
            Root = Root.ReplaceNodes(NodesToMark, (OldNode, NewNode) => NewNode.WithAdditionalAnnotations(ToRemoveAnnotation));

            // 第二阶段：执行语法树重写
            // 传入的谓词 Node => Node.HasAnnotation(ToRemoveAnnotation) 用于告知重写器哪些节点已被标记为移除
            var Rewriter = new ExpressionSimplifier(Node => Node.HasAnnotation(ToRemoveAnnotation), Model, NodesToMark);
            return Rewriter.Visit(Root);
        }

        /// <summary>
        /// 收集并传播需要移除的节点标记。
        /// 该方法首先找到所有直接满足谓词条件的节点，然后通过迭代传播：
        /// - 向上收集：标记父节点（如包含已标记初始值的变量声明）。
        /// - 引用传播：通过语义模型标记所有对已标记变量的引用。
        /// </summary>
        /// <param name="Root">语法树根节点</param>
        /// <param name="ShouldRemove">初始判定谓词</param>
        /// <param name="Model">语义模型</param>
        /// <returns>最终确定的待移除节点集合</returns>
        private static System.Collections.Generic.HashSet<SyntaxNode> CollectNodesToMark(SyntaxNode Root, System.Func<SyntaxNode, bool> ShouldRemove, SemanticModel? Model)
        {
            var NodesToMark = new System.Collections.Generic.HashSet<SyntaxNode>();

            // 1. 初始收集：找到所有直接符合谓词条件的节点
            foreach (var Node in Root.DescendantNodesAndSelf())
            {
                if (ShouldRemove(Node))
                {
                    NodesToMark.Add(Node);
                }
            }

            // 2. 迭代传播：直到标记集合不再发生变化
            int LastCount;

            // 初始化语义传播器（如果提供了语义模型）
            // 方案三：符号-引用映射预处理。预先构建映射表以提高传播效率。
            PreprocessedSymbolPropagator? semanticPropagator = null;
            if (Model != null)
            {
                semanticPropagator = new PreprocessedSymbolPropagator(Model, NodesToMark, Root);
            }

            do
            {
                LastCount = NodesToMark.Count;
                // 结构性传播：根据语法结构向上标记父级节点。
                // 优化：仅在包含已标记节点的"功能作用域"内执行向上传播，避免全树扫描并确定传播边界。
                var relevantScopes = NodesToMark
                    .Select(n => n.AncestorsAndSelf().FirstOrDefault(a =>
                        a is BaseMethodDeclarationSyntax ||
                        a is AnonymousFunctionExpressionSyntax ||
                        a is LocalFunctionStatementSyntax ||
                        a is BaseFieldDeclarationSyntax ||
                        a is BasePropertyDeclarationSyntax))
                    .Where(s => s != null)
                    .Distinct()
                    .ToList();

                var UpwardCollector = new UpwardMarkCollector(NodesToMark);
                if (relevantScopes.Count > 0)
                {
                    foreach (var scope in relevantScopes)
                    {
                        UpwardCollector.Visit(scope);
                    }
                }
                else
                {
                    // 如果没有任何功能作用域（例如标记了全局的 using 或命名空间），则降级为全树扫描
                    UpwardCollector.Visit(Root);
                }

                // 语义传播：分析变量引用，将已标记变量的所有使用处也标记为移除
                semanticPropagator?.Propagate();

            } while (NodesToMark.Count > LastCount);

            return NodesToMark;
        }
    }
}
