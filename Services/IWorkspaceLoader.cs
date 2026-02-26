using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace TerrariaTools.Services
{
    public interface IWorkspaceLoader
    {
        Solution? CurrentSolution { get; }

        Task<Solution?> LoadSolutionAsync(string path);
        Task<Compilation?> LoadTerrariaProjectAsync(string path);

        Task<Dictionary<Project, List<SemanticModel>>> LoadSolutionSemanticModelsAsync();
        Task<List<SyntaxTree>> LoadAllSyntaxTreesAsync();

        Task<SyntaxTree?> LoadFileSyntaxTreeAsync(string filePath);
        bool CheckExists(string filePath);
        Task<SemanticModel?> GetFileSemanticModelAsync(string filePath);

        IEnumerable<IPropertySymbol> GetPropertiesFromSemanticModel(SemanticModel semanticModel);
        IEnumerable<IFieldSymbol> GetFieldsFromSemanticModel(SemanticModel semanticModel);
        IEnumerable<IMethodSymbol> GetMethodsFromSemanticModel(SemanticModel semanticModel);
        IEnumerable<IModuleSymbol> GetModulesFromSemanticModel(SemanticModel semanticModel);
        IEnumerable<INamedTypeSymbol> GetNamedTypesFromSemanticModel(SemanticModel semanticModel);

        Task<SyntaxNode?> FindSymbolsReferencesAsync(SemanticModel model, List<(ISymbol symbol, SyntaxAnnotation annotation)> symbolsToFind);

        Task SaveDocumentAsync(string filePath, string content);
    }
}
