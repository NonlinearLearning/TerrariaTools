/**
 * 该文件提供了加载和分析 Visual Studio 解决方案的功能。
 * 包含加载解决方案、获取语义模型、语法树以及提取各类符号（如属性、方法、命名空间等）的方法。
 * 同时支持在语义模型中查找和标记方法引用。
 */

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Build.Locator;

namespace TerrariaTools
{
    /// <summary>
    /// 提供解决方案加载和分析功能的类。
    /// </summary>
    public class Load
    {
        /// <summary>
        /// 异步加载解决方案并返回打开的工作区。
        /// </summary>
        /// <param name="solutionPath">解决方案路径。</param>
        /// <returns>返回 MSBuildWorkspace 对象，如果加载失败则返回 null。</returns>
        public async Task<MSBuildWorkspace?> LoadSolutionAsync(string solutionPath)
        {
            // 1. 注册 MSBuild 实例
            // 这是使用 MSBuildWorkspace 之前的必要步骤，用于定位系统中安装的 MSBuild/SDK。
            if (!MSBuildLocator.IsRegistered)
            {
                try
                {
                    MSBuildLocator.RegisterDefaults();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"注册 MSBuild 失败: {ex.Message}");
                }
            }

            // 2. 创建 MSBuild 工作区
            // 这是一个重量级对象，负责管理项目依赖和内存中的代码模型。
            var workspace = MSBuildWorkspace.Create();
            // 监听工作区诊断信息，便于调试加载过程中的失败原因（如缺少 SDK、NuGet 包未还原等）。
            workspace.RegisterWorkspaceFailedHandler(args =>
            {
                Console.WriteLine($"[工作区诊断] {args.Diagnostic.Kind}: {args.Diagnostic.Message}");
            });

            try
            {
                // 3. 打开解决方案文件
                // 这是一个耗时的异步过程，会解析 .sln 文件及其包含的所有 .csproj 文件。
                await workspace.OpenSolutionAsync(solutionPath);
                return workspace;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"打开解决方案时发生异常: {ex.Message}");
                workspace.Dispose();
                return null;
            }
        }
        /// <summary>
        /// 异步加载解决方案中所有项目的语义模型。
        /// </summary>
        /// <param name="workspace">MSBuildWorkspace 对象。</param>
        /// <returns>返回包含所有项目及其对应语义模型列表的字典。</returns>
        public async Task<System.Collections.Generic.Dictionary<Project, System.Collections.Generic.List<SemanticModel>>> LoadSolutionSemanticModelsAsync(MSBuildWorkspace workspace)
        {
            var result = new System.Collections.Generic.Dictionary<Project, System.Collections.Generic.List<SemanticModel>>();
            var solution = workspace.CurrentSolution;

            foreach (var project in solution.Projects)
            {
                var models = new System.Collections.Generic.List<SemanticModel>();
                foreach (var document in project.Documents)
                {
                    var model = await document.GetSemanticModelAsync();
                    if (model != null)
                    {
                        models.Add(model);
                    }
                }
                result[project] = models;
            }

            return result;
        }

        /// <summary>
        /// 异步加载工作区中所有文档的语法树。
        /// </summary>
        /// <param name="workspace">MSBuildWorkspace 对象。</param>
        /// <returns>返回包含所有文档及其对应语法树的列表。</returns>
        public async Task<System.Collections.Generic.List<SyntaxTree>> LoadAllSyntaxTreesAsync(MSBuildWorkspace workspace)
        {
            var result = new System.Collections.Generic.List<SyntaxTree>();
            if (workspace == null || workspace.CurrentSolution == null) return result;

            foreach (var project in workspace.CurrentSolution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    var tree = await document.GetSyntaxTreeAsync();
                    if (tree != null)
                    {
                        result.Add(tree);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 异步加载工作区中指定文件的语法树。
        /// </summary>
        /// <param name="workspace">MSBuildWorkspace 对象。</param>
        /// <param name="filePath">文件路径。</param>
        /// <returns>返回指定文件的语法树，如果找不到则返回 null。</returns>
        public async Task<SyntaxTree?> LoadFileSyntaxTreeAsync(MSBuildWorkspace workspace, string filePath)
        {
            if (workspace == null || workspace.CurrentSolution == null || string.IsNullOrWhiteSpace(filePath)) return null;

            string fullPath = System.IO.Path.GetFullPath(filePath);
            var solution = workspace.CurrentSolution;

            // 优化：直接根据路径获取文档 ID
            var documentId = solution.GetDocumentIdsWithFilePath(fullPath).FirstOrDefault();
            if (documentId == null) return null;

            var document = solution.GetDocument(documentId);
            if (document == null) return null;

            return await document.GetSyntaxTreeAsync();
        }
        /// <summary>
        /// 根据传入的工作区检索某个文件是否存在于当前解决方案中。
        /// </summary>
        /// <param name="workspace">MSBuildWorkspace 对象。</param>
        /// <param name="filePath">要检索的文件路径（可以是绝对路径或相对路径）。</param>
        /// <returns>如果文件存在于解决方案的任何项目中，则返回 true。</returns>
        public bool CheckExists(MSBuildWorkspace workspace, string filePath)
        {
            if (workspace == null || workspace.CurrentSolution == null || string.IsNullOrWhiteSpace(filePath)) return false;

            string fullPath = System.IO.Path.GetFullPath(filePath);
            var solution = workspace.CurrentSolution;

            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (document.FilePath != null && System.IO.Path.GetFullPath(document.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 异步加载指定文件的语义模型。
        /// </summary>
        /// <param name="workspace">MSBuildWorkspace 对象。</param>
        /// <param name="filePath">文件路径。</param>
        /// <returns>返回文件的语义模型，如果找不到文件则返回 null。</returns>
        public async Task<SemanticModel?> GetFileSemanticModelAsync(MSBuildWorkspace workspace, string filePath)
        {
            if (workspace == null || workspace.CurrentSolution == null || string.IsNullOrWhiteSpace(filePath)) return null;

            string fullPath = System.IO.Path.GetFullPath(filePath);
            var solution = workspace.CurrentSolution;

            foreach (var project in solution.Projects)
            {
                var document = project.Documents.FirstOrDefault(d =>
                    d.FilePath != null && System.IO.Path.GetFullPath(d.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));

                if (document != null)
                {
                    return await document.GetSemanticModelAsync();
                }
            }

            return null;
        }


        /// <summary>
        /// 从指定的语义模型中提取所有属性符号。
        /// </summary>
        /// <param name="semanticModel">语义模型对象。</param>
        /// <returns>返回该语义模型中定义的所有属性符号列表。</returns>
        public System.Collections.Generic.IEnumerable<IPropertySymbol> GetPropertiesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<IPropertySymbol>();

            // 获取语法树根节点
            var root = semanticModel.SyntaxTree.GetRoot();

            // 查找所有声明的符号，并过滤出属性符号
            var symbols = root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .Where(symbol => symbol is IPropertySymbol)
                .Cast<IPropertySymbol>()
                .Distinct<IPropertySymbol>(SymbolEqualityComparer.Default);

            return symbols;
        }

        /// <summary>
        /// 获取语义模型中的所有字段
        /// </summary>
        /// <param name="semanticModel">语义模型</param>
        /// <returns>字段符号集合</returns>
        public System.Collections.Generic.IEnumerable<IFieldSymbol> GetFieldsFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<IFieldSymbol>();

            // 获取语法树根节点
            var root = semanticModel.SyntaxTree.GetRoot();

            // 查找所有声明的符号，并过滤出字段符号
            var symbols = root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .Where(symbol => symbol is IFieldSymbol)
                .Cast<IFieldSymbol>()
                .Distinct<IFieldSymbol>(SymbolEqualityComparer.Default);

            return symbols;
        }

        /// <summary>
        /// 获取语义模型中的所有方法
        /// </summary>
        /// <param name="semanticModel">语义模型</param>
        /// <returns>方法符号集合</returns>
        public System.Collections.Generic.IEnumerable<IMethodSymbol> GetMethodsFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<IMethodSymbol>();

            // 获取语法树根节点
            var root = semanticModel.SyntaxTree.GetRoot();

            // 查找所有声明的符号，并过滤出方法符号
            var symbols = root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .Where(symbol => symbol is IMethodSymbol)
                .Cast<IMethodSymbol>()
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);

            return symbols;
        }

        /// <summary>
        /// 获取语义模型所属程序集中的所有模块
        /// </summary>
        /// <param name="semanticModel">语义模型</param>
        /// <returns>模块符号集合</returns>
        public System.Collections.Generic.IEnumerable<IModuleSymbol> GetModulesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<IModuleSymbol>();

            // 模块通常是程序集的一部分，而不是在语法树中声明的
            return semanticModel.Compilation.Assembly.Modules;
        }

        /// <summary>
        /// 获取语义模型中的所有具名类型（类、接口、结构体、枚举、委托）
        /// </summary>
        /// <param name="semanticModel">语义模型</param>
        /// <returns>具名类型符号集合</returns>
        public System.Collections.Generic.IEnumerable<INamedTypeSymbol> GetNamedTypesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<INamedTypeSymbol>();

            // 获取语法树根节点
            var root = semanticModel.SyntaxTree.GetRoot();

            // 查找所有声明的符号，并过滤出具名类型符号
            var symbols = root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .Where(symbol => symbol is INamedTypeSymbol)
                .Cast<INamedTypeSymbol>()
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            return symbols;
        }

        /// <summary>
        /// 获取语义模型中的所有命名空间
        /// </summary>
        /// <param name="semanticModel">语义模型</param>
        /// <returns>命名空间符号集合</returns>
        public System.Collections.Generic.IEnumerable<INamespaceSymbol> GetNamespacesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<INamespaceSymbol>();

            // 获取语法树根节点
            var root = semanticModel.SyntaxTree.GetRoot();

            // 查找所有声明的符号，并过滤出命名空间符号
            var symbols = root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .Where(symbol => symbol is INamespaceSymbol)
                .Cast<INamespaceSymbol>()
                .Distinct<INamespaceSymbol>(SymbolEqualityComparer.Default);

            return symbols;
        }

        /// <summary>
        /// 获取语义模型中的所有动态类型
        /// </summary>
        /// <param name="semanticModel">语义模型</param>
        /// <returns>动态类型符号集合</returns>
        public System.Collections.Generic.IEnumerable<IDynamicTypeSymbol> GetDynamicTypesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return System.Linq.Enumerable.Empty<IDynamicTypeSymbol>();

            // 获取语法树根节点
            var root = semanticModel.SyntaxTree.GetRoot();

            // 查找所有节点，获取其类型信息，并过滤出动态类型符号
            var symbols = root.DescendantNodes()
                .Select(node => semanticModel.GetTypeInfo(node).Type)
                .Where(type => type is IDynamicTypeSymbol)
                .Cast<IDynamicTypeSymbol>()
                .Distinct<IDynamicTypeSymbol>(SymbolEqualityComparer.Default);

            return symbols;
        }

        /// <summary>
        /// 异步加载指定文件的语义模型及其外部依赖项。
        /// 该方法通过直接定位文档，避免加载整个工作区的语义模型，仅分析目标文件所需的上下文。
        /// </summary>
        /// <param name="workspace">MSBuildWorkspace 对象。</param>
        /// <param name="filePath">文件路径。</param>
        /// <returns>返回一个元组，包含文件的语义模型和提取出的外部依赖符号列表。</returns>
        public async Task<(SemanticModel? Model, System.Collections.Generic.IEnumerable<ISymbol> Dependencies)> LoadFileSemanticModelWithDependenciesAsync(MSBuildWorkspace workspace, string filePath)
        {
            var model = await GetFileSemanticModelAsync(workspace, filePath);
            if (model == null) return (null, System.Linq.Enumerable.Empty<ISymbol>());

            var root = await model.SyntaxTree.GetRootAsync();

            // 提取所有节点关联的符号，并过滤出外部依赖（不在当前程序集中的符号）
            var dependencies = root.DescendantNodes()
                .Select(node => model.GetSymbolInfo(node).Symbol)
                .Where(symbol => symbol != null &&
                                symbol.ContainingAssembly != null &&
                                !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, model.Compilation.Assembly))
                .Select(symbol => symbol!) // 显式转换以消除 null 性警告
                .Distinct(SymbolEqualityComparer.Default);

            return (model, dependencies);
        }

        /// <summary>
        /// 在指定的语义模型中查找方法的所有引用，并在源码节点上添加 SyntaxAnnotation 标记。
        /// </summary>
        /// <param name="semanticModel">语义模型对象。</param>
        /// <param name="methodSymbol">要查找引用的方法符号。</param>
        /// <param name="annotation">用于标记引用的 SyntaxAnnotation 对象。</param>
        /// <returns>返回修改后的语法树根节点。</returns>
        public async Task<SyntaxNode?> FindMethodReferencesAsync(SemanticModel semanticModel, IMethodSymbol methodSymbol, SyntaxAnnotation annotation)
        {
            return await FindSymbolReferencesInternalAsync(semanticModel, methodSymbol, annotation);
        }

        /// <summary>
        /// 在指定的语义模型中查找字段的所有引用，并在源码节点上添加 SyntaxAnnotation 标记。
        /// </summary>
        /// <param name="semanticModel">语义模型对象。</param>
        /// <param name="fieldSymbol">要查找引用的字段符号。</param>
        /// <param name="annotation">用于标记引用的 SyntaxAnnotation 对象。</param>
        /// <returns>返回修改后的语法树根节点。</returns>
        public async Task<SyntaxNode?> FindFieldReferencesAsync(SemanticModel semanticModel, IFieldSymbol fieldSymbol, SyntaxAnnotation annotation)
        {
            return await FindSymbolReferencesInternalAsync(semanticModel, fieldSymbol, annotation);
        }

        /// <summary>
        /// 在指定的语义模型中查找属性的所有引用，并在源码节点上添加 SyntaxAnnotation 标记。
        /// </summary>
        /// <param name="semanticModel">语义模型对象。</param>
        /// <param name="propertySymbol">要查找引用的属性符号。</param>
        /// <param name="annotation">用于标记引用的 SyntaxAnnotation 对象。</param>
        /// <returns>返回修改后的语法树根节点。</returns>
        public async Task<SyntaxNode?> FindPropertyReferencesAsync(SemanticModel semanticModel, IPropertySymbol propertySymbol, SyntaxAnnotation annotation)
        {
            return await FindSymbolReferencesInternalAsync(semanticModel, propertySymbol, annotation);
        }

        /// <summary>
        /// 查找并标记多个符号的引用。
        /// 该方法在 Main 函数的 CodeProcess 步骤中被调用，用于在并行处理文档时批量标记目标符号。
        /// </summary>
        /// <param name="SemanticModel">语义模型</param>
        /// <param name="SymbolAnnotations">符号及其对应的标记集合</param>
        /// <returns>带有标记的语法树根节点</returns>
        public async Task<SyntaxNode?> FindSymbolsReferencesAsync(SemanticModel SemanticModel, System.Collections.Generic.IEnumerable<(ISymbol Symbol, SyntaxAnnotation Annotation)> SymbolAnnotations)
        {
            if (SemanticModel == null || SymbolAnnotations == null)
                return null;

            var Root = await SemanticModel.SyntaxTree.GetRootAsync();
            if (Root == null) return null;

            // 创建多符号重写器实例
            var Rewriter = new MultiSymbolReferenceRewriter(SemanticModel, SymbolAnnotations);
            return (SyntaxNode?)Rewriter.Visit(Root);
        }

        /// <summary>
        /// 内部通用的符号引用查找和标记方法。
        /// </summary>
        /// <param name="SemanticModel">语义模型</param>
        /// <param name="Symbol">要查找的符号</param>
        /// <param name="Annotation">对应的注解标记</param>
        /// <returns>带有标记的语法树根节点</returns>
        private async Task<SyntaxNode?> FindSymbolReferencesInternalAsync(SemanticModel SemanticModel, ISymbol Symbol, SyntaxAnnotation Annotation)
        {
            if (SemanticModel == null || Symbol == null || Annotation == null)
                return null;

            var Root = await SemanticModel.SyntaxTree.GetRootAsync();
            if (Root == null) return null;

            // 使用通用的符号引用重写器
            var Rewriter = new SymbolReferenceRewriter(SemanticModel, Symbol, Annotation);
            return Rewriter.Visit(Root);
        }

        /// <summary>
        /// 多符号引用重写器，用于在一次遍历中为多个符号的所有引用添加标记。
        /// 该类主要用于高性能代码处理流程，在 Main 函数中配合并行文档处理使用。
        /// 通过批量标记符号，可以在后续步骤中一次性处理所有相关引用（如移除无效代码）。
        /// </summary>
        private class MultiSymbolReferenceRewriter : CSharpSyntaxRewriter
        {
            /// <summary>
            /// 语义模型，用于获取节点的符号信息。
            /// </summary>
            private readonly SemanticModel SemanticModel;

            /// <summary>
            /// 符号与对应标记的映射表。
            /// </summary>
            private readonly System.Collections.Generic.Dictionary<ISymbol, SyntaxAnnotation> SymbolMap;

            /// <summary>
            /// 初始化 MultiSymbolReferenceRewriter 的新实例。
            /// </summary>
            /// <param name="SemanticModel">当前文档的语义模型</param>
            /// <param name="SymbolAnnotations">需要标记的符号及其对应的注解集合</param>
            public MultiSymbolReferenceRewriter(SemanticModel SemanticModel, System.Collections.Generic.IEnumerable<(ISymbol Symbol, SyntaxAnnotation Annotation)> SymbolAnnotations)
            {
                this.SemanticModel = SemanticModel;
                this.SymbolMap = new System.Collections.Generic.Dictionary<ISymbol, SyntaxAnnotation>(SymbolEqualityComparer.Default);
                foreach (var Item in SymbolAnnotations)
                {
                    if (Item.Symbol != null)
                    {
                        this.SymbolMap[Item.Symbol] = Item.Annotation;
                    }
                }
            }

            /// <summary>
            /// 访问并处理语法节点。
            /// 如果节点关联的符号在映射表中，则为其添加相应的注解。
            /// </summary>
            /// <param name="Node">待访问的语法节点</param>
            /// <returns>处理后（可能带有注解）的语法节点</returns>
            public override SyntaxNode? Visit(SyntaxNode? Node)
            {
                if (Node == null) return null;

                // 检查节点关联的符号是否存在于待标记映射中
                var NodeToQuery = GetQueryableNode(Node, this.SemanticModel);
                if (NodeToQuery.SyntaxTree != this.SemanticModel.SyntaxTree)
                {
                    return base.Visit(Node);
                }

                var Symbol = this.SemanticModel.GetSymbolInfo(NodeToQuery).Symbol;
                if (Symbol != null && this.SymbolMap.TryGetValue(Symbol, out var Annotation))
                {
                    // 为节点添加标记，以便后续工具类识别并处理
                    return Node.WithAdditionalAnnotations(Annotation);
                }

                return base.Visit(Node);
            }

            /// <summary>
            /// 安全获取可用于语义查询的节点。
            /// </summary>
            private SyntaxNode GetQueryableNode(SyntaxNode Node, SemanticModel Model)
            {
                if (Model == null || Node.SyntaxTree == Model.SyntaxTree) return Node;
                try
                {
                    var OriginalRoot = Model.SyntaxTree.GetRoot();
                    if (Node.FullSpan.End <= OriginalRoot.FullSpan.End)
                    {
                        return OriginalRoot.FindNode(Node.FullSpan, getInnermostNodeForTie: true);
                    }
                }
                catch { }
                return Node;
            }
        }

        /// <summary>
        /// 单符号引用重写器，用于在语法树节点上为特定符号添加标记。
        /// 这是 SymbolReferenceRewriter 的基础实现，适用于处理单个目标的场景。
        /// </summary>
        private class SymbolReferenceRewriter : CSharpSyntaxRewriter
        {
            /// <summary>
            /// 语义模型，用于符号解析。
            /// </summary>
            private readonly SemanticModel SemanticModel;

            /// <summary>
            /// 目标符号，所有对该符号的引用都将被标记。
            /// </summary>
            private readonly ISymbol TargetSymbol;

            /// <summary>
            /// 要添加的语法标记。
            /// </summary>
            private readonly SyntaxAnnotation Annotation;

            /// <summary>
            /// 初始化 SymbolReferenceRewriter 的新实例。
            /// </summary>
            /// <param name="SemanticModel">语义模型</param>
            /// <param name="TargetSymbol">目标符号</param>
            /// <param name="Annotation">对应的注解</param>
            public SymbolReferenceRewriter(SemanticModel SemanticModel, ISymbol TargetSymbol, SyntaxAnnotation Annotation)
            {
                this.SemanticModel = SemanticModel;
                this.TargetSymbol = TargetSymbol;
                this.Annotation = Annotation;
            }

            /// <summary>
            /// 访问节点并检查其是否引用了目标符号。
            /// </summary>
            /// <param name="Node">语法节点</param>
            /// <returns>处理后的语法节点</returns>
            public override SyntaxNode? Visit(SyntaxNode? Node)
            {
                if (Node == null) return null;

                // 检查节点关联的符号是否与目标符号一致
                var NodeToQuery = GetQueryableNode(Node, this.SemanticModel);
                if (NodeToQuery.SyntaxTree != this.SemanticModel.SyntaxTree)
                {
                    return base.Visit(Node);
                }

                var Symbol = this.SemanticModel.GetSymbolInfo(NodeToQuery).Symbol;
                if (SymbolEqualityComparer.Default.Equals(Symbol, this.TargetSymbol))
                {
                    // 命中目标符号引用，添加标记
                    return Node.WithAdditionalAnnotations(this.Annotation);
                }

                return base.Visit(Node);
            }

            /// <summary>
            /// 安全获取可用于语义查询的节点。
            /// </summary>
            private SyntaxNode GetQueryableNode(SyntaxNode Node, SemanticModel Model)
            {
                if (Model == null || Node.SyntaxTree == Model.SyntaxTree) return Node;
                try
                {
                    var OriginalRoot = Model.SyntaxTree.GetRoot();
                    if (Node.FullSpan.End <= OriginalRoot.FullSpan.End)
                    {
                        return OriginalRoot.FindNode(Node.FullSpan, getInnermostNodeForTie: true);
                    }
                }
                catch { }
                return Node;
            }
        }


    }
}