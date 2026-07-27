using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// 带初始化值的定义结构分析结果。
public sealed record AssignmentDefinitionAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// 分析变量定义和赋值同时出现的结构，例如 <c>int value = seed + 1</c>。
public sealed class AssignmentDefinitionAnalyzer
{
    private sealed record AssignmentDefinitionStructure(
        SyntaxNode Root,
        SyntaxNode? Declaration,
        SyntaxNode? Type,
        SyntaxNode Initializer,
        SyntaxNode Value);

    // 提取带初始化值定义的局部结构节点，供 mark 和 propagation 规则复用。
    public AssignmentDefinitionAnalysis Analyze(VariableDeclaratorSyntax root, CpgAnalysisContext context)
    {
        if (root.Initializer is null)
        {
            throw new ArgumentException("Variable declarator must have an initializer.", nameof(root));
        }

        _ = context;

        var structure = new AssignmentDefinitionStructure(
            GetAnalysisRoot(root),
            root.Parent,
            root.Parent is VariableDeclarationSyntax declaration ? declaration.Type : null,
            root.Initializer,
            root.Initializer.Value);
        var affectedNodes = new List<SyntaxNode>
        {
            structure.Root,
            structure.Initializer,
            structure.Value
        };

        if (structure.Declaration is VariableDeclarationSyntax variableDeclaration)
        {
            affectedNodes.Add(variableDeclaration);
            if (structure.Type is not null)
            {
                affectedNodes.Add(structure.Type);
            }
        }

        return new AssignmentDefinitionAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, affectedNodes));
    }

    private static SyntaxNode GetAnalysisRoot(VariableDeclaratorSyntax root)
    {
        return root.Parent is VariableDeclarationSyntax declaration ? declaration : root;
    }
}
