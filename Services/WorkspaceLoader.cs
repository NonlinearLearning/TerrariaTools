using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Generic;

namespace TerrariaTools.Services
{
    /// <summary>
    /// 提供统一的 Roslyn 工作区加载服务，处理 .sln/.csproj 加载及项目筛选逻辑。
    /// </summary>
    public class WorkspaceLoader : IWorkspaceLoader, IDisposable
    {
        private MSBuildWorkspace? _workspace;

        /// <summary>
        /// 获取当前加载的解决方案。
        /// </summary>
        public Solution? CurrentSolution { get; private set; }

        /// <summary>
        /// 加载指定路径的解决方案。
        /// </summary>
        /// <param name="path">解决方案 (.sln) 文件路径</param>
        /// <returns>加载的 Solution 对象，如果失败则返回 null</returns>
        public async Task<Solution?> LoadSolutionAsync(string path)
        {
            if (_workspace != null)
            {
                _workspace.Dispose();
            }

            Console.WriteLine($"[Loader] 正在初始化工作区: {path}...");
            _workspace = MSBuildWorkspace.Create();
            _workspace.SkipUnrecognizedProjects = true;
            _workspace.LoadMetadataForReferencedProjects = true;

            // _workspace.WorkspaceFailed += (sender, args) =>
            // {
            //     Console.WriteLine($"[工作区诊断] {args.Diagnostic.Kind}: {args.Diagnostic.Message}");
            // };
            // 修复 CS0618 过时警告
            _workspace.RegisterWorkspaceFailedHandler(args =>
            {
                Console.WriteLine($"[工作区诊断] {args.Diagnostic.Kind}: {args.Diagnostic.Message}");
            });

            try
            {
                CurrentSolution = await _workspace.OpenSolutionAsync(path);
                return CurrentSolution;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Loader] 加载解决方案失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 加载指定路径的解决方案或项目，并返回目标项目的编译单元。
        /// 优先查找名为 "Terraria" 或 "TerrariaServer" 的项目。
        /// </summary>
        /// <param name="path">解决方案 (.sln) 或项目 (.csproj) 的文件路径</param>
        /// <returns>目标项目的 Compilation 对象，如果加载失败则返回 null</returns>
        public async Task<Compilation?> LoadTerrariaProjectAsync(string path)
        {
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await LoadSolutionAsync(path);
                if (solution == null) return null;

                // 优先查找 Terraria 项目，如果没找到则尝试找第一个项目
                var project = solution.Projects.FirstOrDefault(p => p.Name == "Terraria" || p.Name == "TerrariaServer")
                              ?? solution.Projects.FirstOrDefault();

                if (project != null)
                {
                    Console.WriteLine($"[Loader] 已选择项目: {project.Name}");
                    return await project.GetCompilationAsync();
                }
                return null;
            }
            else
            {
                if (_workspace != null)
                {
                    _workspace.Dispose();
                }

                Console.WriteLine($"[Loader] 正在初始化工作区: {path}...");

                _workspace = MSBuildWorkspace.Create();

                // 忽略加载错误，仅关注能加载的部分
                _workspace.SkipUnrecognizedProjects = true;
                _workspace.LoadMetadataForReferencedProjects = true;

                try
                {
                    var project = await _workspace.OpenProjectAsync(path);
                    CurrentSolution = project.Solution;
                    return await project.GetCompilationAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Loader] 加载项目失败: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 异步加载解决方案中所有项目的语义模型。
        /// </summary>
        /// <returns>返回包含所有项目及其对应语义模型列表的字典。</returns>
        public async Task<Dictionary<Project, List<SemanticModel>>> LoadSolutionSemanticModelsAsync()
        {
            var result = new Dictionary<Project, List<SemanticModel>>();

            if (CurrentSolution == null)
            {
                return result;
            }

            foreach (var project in CurrentSolution.Projects)
            {
                var models = new List<SemanticModel>();
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
        /// <returns>返回包含所有文档及其对应语法树的列表。</returns>
        public async Task<List<SyntaxTree>> LoadAllSyntaxTreesAsync()
        {
            var result = new List<SyntaxTree>();

            if (CurrentSolution == null)
            {
                return result;
            }

            foreach (var project in CurrentSolution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree != null)
                    {
                        result.Add(syntaxTree);
                    }
                }
            }
            return result;
        }

        public async Task<SyntaxTree?> LoadFileSyntaxTreeAsync(string filePath)
        {
            if (CurrentSolution == null || string.IsNullOrWhiteSpace(filePath)) return null;

            string fullPath = System.IO.Path.GetFullPath(filePath);

            // 优化：直接根据路径获取文档 ID
            var documentId = CurrentSolution.GetDocumentIdsWithFilePath(fullPath).FirstOrDefault();
            if (documentId == null) return null;

            var document = CurrentSolution.GetDocument(documentId);
            if (document == null) return null;

            return await document.GetSyntaxTreeAsync();
        }

        public bool CheckExists(string filePath)
        {
            if (CurrentSolution == null || string.IsNullOrWhiteSpace(filePath)) return false;

            string fullPath = System.IO.Path.GetFullPath(filePath);

            foreach (var project in CurrentSolution.Projects)
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

        public async Task<SemanticModel?> GetFileSemanticModelAsync(string filePath)
        {
            if (CurrentSolution == null || string.IsNullOrWhiteSpace(filePath)) return null;

            string fullPath = System.IO.Path.GetFullPath(filePath);

            foreach (var project in CurrentSolution.Projects)
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

        public IEnumerable<IPropertySymbol> GetPropertiesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return Enumerable.Empty<IPropertySymbol>();

            var root = semanticModel.SyntaxTree.GetRoot();
            return root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .OfType<IPropertySymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<IPropertySymbol>();
        }

        public IEnumerable<IFieldSymbol> GetFieldsFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return Enumerable.Empty<IFieldSymbol>();

            var root = semanticModel.SyntaxTree.GetRoot();
            return root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .OfType<IFieldSymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<IFieldSymbol>();
        }

        public IEnumerable<IMethodSymbol> GetMethodsFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return Enumerable.Empty<IMethodSymbol>();

            var root = semanticModel.SyntaxTree.GetRoot();
            return root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .OfType<IMethodSymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<IMethodSymbol>();
        }

        public IEnumerable<IModuleSymbol> GetModulesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return Enumerable.Empty<IModuleSymbol>();
            return semanticModel.Compilation.Assembly.Modules;
        }

        public IEnumerable<INamedTypeSymbol> GetNamedTypesFromSemanticModel(SemanticModel semanticModel)
        {
            if (semanticModel == null) return Enumerable.Empty<INamedTypeSymbol>();

            var root = semanticModel.SyntaxTree.GetRoot();
            return root.DescendantNodes()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .OfType<INamedTypeSymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<INamedTypeSymbol>();
        }

        public async Task<SyntaxNode?> FindSymbolsReferencesAsync(SemanticModel model, List<(ISymbol symbol, SyntaxAnnotation annotation)> symbolsToFind)
        {
            if (model == null || symbolsToFind == null || symbolsToFind.Count == 0) return null;

            var root = await model.SyntaxTree.GetRootAsync();
            var nodesToAnnotate = new Dictionary<SyntaxNode, SyntaxAnnotation>();

            foreach (var node in root.DescendantNodes())
            {
                var symbolInfo = model.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol;

                if (symbol == null) continue;

                foreach (var item in symbolsToFind)
                {
                    if (SymbolEqualityComparer.Default.Equals(symbol, item.symbol))
                    {
                        if (!nodesToAnnotate.ContainsKey(node))
                        {
                            nodesToAnnotate[node] = item.annotation;
                        }
                    }
                }
            }

            if (nodesToAnnotate.Count > 0)
            {
                // Note: ReplaceNodes replaces the nodes in the tree.
                // Since we are iterating and replacing based on original nodes, this works.
                // However, if we have nested nodes to replace, ReplaceNodes handles it correctly (bottom-up).

                return root.ReplaceNodes(nodesToAnnotate.Keys, (original, rewritten) =>
                {
                    if (nodesToAnnotate.TryGetValue(original, out var annotation))
                    {
                        return rewritten.WithAdditionalAnnotations(annotation);
                    }
                    return rewritten;
                });
            }

            return null;
        }

        public async Task SaveDocumentAsync(string filePath, string content)
        {
            await System.IO.File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);
        }

        public void Dispose()
        {
            _workspace?.Dispose();
        }
    }
}
