using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynPrototype.Analysis;

/// <summary>
/// 循环结构分析结果。
/// </summary>
public sealed record LoopStructureAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析 C# 常见循环结构，包括 <c>for</c>、<c>foreach</c>、<c>while</c> 和 <c>do</c>。
/// </summary>
public sealed class LoopStructureAnalyzer
{
    private sealed record LoopStructure(
        StatementSyntax Root,
        IReadOnlyList<SyntaxNode> Members);

    /// <summary>
    /// 返回循环头部、条件、迭代器、集合表达式和循环体等关键语法节点。
    /// </summary>
    public LoopStructureAnalysis Analyze(StatementSyntax root, CpgAnalysisContext context)
    {
        _ = context;

        var structure = root switch
        {
            ForStatementSyntax forStatement => AnalyzeForStatement(forStatement),
            ForEachStatementSyntax forEachStatement => AnalyzeForEachStatement(forEachStatement),
            ForEachVariableStatementSyntax forEachVariableStatement => AnalyzeForEachVariableStatement(forEachVariableStatement),
            WhileStatementSyntax whileStatement => AnalyzeWhileStatement(whileStatement),
            DoStatementSyntax doStatement => AnalyzeDoStatement(doStatement),
            _ => throw new ArgumentException("Loop root must be for, foreach, while, or do statement.", nameof(root))
        };

        return new LoopStructureAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, structure.Members));
    }

    private static LoopStructure AnalyzeForStatement(ForStatementSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Declaration);
        nodes.AddRange(root.Initializers);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Condition);
        nodes.AddRange(root.Incrementors);
        nodes.Add(root.Statement);
        return new LoopStructure(root, nodes);
    }

    private static LoopStructure AnalyzeForEachStatement(ForEachStatementSyntax root)
    {
        return new LoopStructure(
            root,
            new SyntaxNode[]
        {
            root,
            root.Type,
            root.Expression,
            root.Statement
        });
    }

    private static LoopStructure AnalyzeForEachVariableStatement(ForEachVariableStatementSyntax root)
    {
        return new LoopStructure(
            root,
            new SyntaxNode[]
        {
            root,
            root.Variable,
            root.Expression,
            root.Statement
        });
    }

    private static LoopStructure AnalyzeWhileStatement(WhileStatementSyntax root)
    {
        return new LoopStructure(
            root,
            new SyntaxNode[]
        {
            root,
            root.Condition,
            root.Statement
        });
    }

    private static LoopStructure AnalyzeDoStatement(DoStatementSyntax root)
    {
        return new LoopStructure(
            root,
            new SyntaxNode[]
        {
            root,
            root.Statement,
            root.Condition
        });
    }
}
