using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace TerrariaTools.Analysis
{
    public class AdvancedCodeAnalyzer
    {
        private readonly Solution _solution;

        public AdvancedCodeAnalyzer(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        public async Task<ArchitectureAnalysisResult> AnalyzeRecursiveDependenciesAsync(string typeName, string methodName)
        {
            var result = new ArchitectureAnalysisResult();
            
            var project = _solution.Projects.FirstOrDefault();
            if (project == null)
            {
                result.Error = "No projects found in solution.";
                return result;
            }

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
            {
                result.Error = "Compilation failed.";
                return result;
            }

            var targetType = compilation.GetTypeByMetadataName(typeName);
            if (targetType == null)
            {
                // Try fuzzy search if exact match fails
                targetType = compilation.GetSymbolsWithName(n => n == typeName.Split('.').Last(), SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault();
                
                if (targetType == null)
                {
                    result.Error = $"Type '{typeName}' not found.";
                    return result;
                }
            }

            var seedSymbol = targetType.GetMembers(methodName).FirstOrDefault();
            if (seedSymbol == null)
            {
                result.Error = $"Method '{methodName}' not found in type '{targetType.ToDisplayString()}'.";
                return result;
            }

            var analyzer = new CodeDependencyAnalyzer(_solution);
            await analyzer.AnalyzeRecursiveAsync(seedSymbol);

            var graph = analyzer.Graph;
            result.NodeCount = graph.AllNodes.Count();
            // Edge count calculation requires iterating all nodes
            result.EdgeCount = graph.AllNodes.Sum(n => n.Dependencies.Count);

            // Find SCCs
            var sccs = graph.FindSCCs();
            result.StrongConnectedComponents = sccs
                .Where(s => s.Count > 1)
                .Select(s => s.Select(n => n.FullName).ToList())
                .ToList();

            // Topological Sort
            try 
            {
                var sorted = graph.TopologicalSort();
                result.TopologicalSort = sorted.Select(n => n.FullName).ToList();
            }
            catch (InvalidOperationException)
            {
                // Cycle detected, cannot sort
            }

            return result;
        }

        public async Task<List<string>> FindEntryPointsAsync(string classNameFilter = "Main")
        {
            var entryPoints = new List<string>();
            
            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var document in project.Documents)
                {
                    var model = await document.GetSemanticModelAsync();
                    if (model == null) continue;

                    var root = await document.GetSyntaxRootAsync();
                    if (root == null) continue;

                    var classes = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                        .Where(c => c.Identifier.Text.Contains(classNameFilter));

                    foreach (var cls in classes)
                    {
                        var symbol = model.GetDeclaredSymbol(cls);
                        if (symbol != null)
                        {
                            entryPoints.Add(symbol.ToDisplayString());
                        }
                    }
                }
            }

            return entryPoints.Distinct().OrderBy(s => s).ToList();
        }
    }
}
