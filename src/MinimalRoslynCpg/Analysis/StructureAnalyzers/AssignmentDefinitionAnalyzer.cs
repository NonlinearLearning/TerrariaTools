using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 带初始化值的定义结构分析结果。
/// </summary>
public sealed record AssignmentDefinitionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析变量定义和赋值同时出现的结构，例如 <c>int value = seed + 1</c>。
/// </summary>
public sealed class AssignmentDefinitionAnalyzer
{
    /// <summary>
    /// 返回变量定义、类型、初始化子句和初始化表达式组成的受影响语法树。
    /// </summary>
    public AssignmentDefinitionAnalysis Analyze(VariableDeclaratorSyntax root, CpgAnalysisContext context)
    {
        if (root.Initializer is null)
        {
            throw new ArgumentException("Variable declarator must have an initializer.", nameof(root));
        }

        var affectedNodes = new List<SyntaxNode> { root, root.Initializer, root.Initializer.Value };

        if (root.Parent is VariableDeclarationSyntax declaration)
        {
            affectedNodes.Add(declaration);
            affectedNodes.Add(declaration.Type);
        }

        return new AssignmentDefinitionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(GetAnalysisRoot(root), affectedNodes));
    }

    private static SyntaxNode GetAnalysisRoot(VariableDeclaratorSyntax root)
    {
        return root.Parent is VariableDeclarationSyntax declaration ? declaration : root;
    }
}
