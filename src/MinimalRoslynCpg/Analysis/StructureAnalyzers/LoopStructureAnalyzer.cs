using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 循环结构分析结果。
/// </summary>
public sealed record LoopStructureAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析 C# 常见循环结构，包括 <c>for</c>、<c>foreach</c>、<c>while</c> 和 <c>do</c>。
/// </summary>
public sealed class LoopStructureAnalyzer
{
    /// <summary>
    /// 返回循环头部、条件、迭代器、集合表达式和循环体等关键语法节点。
    /// </summary>
    public LoopStructureAnalysis Analyze(StatementSyntax root, CpgAnalysisContext context)
    {
        var affectedNodes = root switch
        {
            ForStatementSyntax forStatement => AnalyzeForStatement(forStatement),
            ForEachStatementSyntax forEachStatement => AnalyzeForEachStatement(forEachStatement),
            ForEachVariableStatementSyntax forEachVariableStatement => AnalyzeForEachVariableStatement(forEachVariableStatement),
            WhileStatementSyntax whileStatement => AnalyzeWhileStatement(whileStatement),
            DoStatementSyntax doStatement => AnalyzeDoStatement(doStatement),
            _ => throw new ArgumentException("Loop root must be for, foreach, while, or do statement.", nameof(root))
        };

        return new LoopStructureAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(root, affectedNodes));
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeForStatement(ForStatementSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Declaration);
        nodes.AddRange(root.Initializers);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Condition);
        nodes.AddRange(root.Incrementors);
        nodes.Add(root.Statement);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeForEachStatement(ForEachStatementSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Type,
            root.Expression,
            root.Statement
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeForEachVariableStatement(ForEachVariableStatementSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Variable,
            root.Expression,
            root.Statement
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeWhileStatement(WhileStatementSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Condition,
            root.Statement
        };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeDoStatement(DoStatementSyntax root)
    {
        return new SyntaxNode[]
        {
            root,
            root.Statement,
            root.Condition
        };
    }
}
