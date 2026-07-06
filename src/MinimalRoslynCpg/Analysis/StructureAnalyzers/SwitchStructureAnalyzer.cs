using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// switch 结构分析结果。
/// </summary>
public sealed record SwitchStructureAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析 <c>switch</c> 语句和 <c>switch</c> 表达式。
/// </summary>
public sealed class SwitchStructureAnalyzer
{
    private sealed record SwitchStructure(
        SyntaxNode Root,
        IReadOnlyList<SyntaxNode> Members);

    /// <summary>
    /// 返回 switch 控制表达式、case/default 标签、语句块或表达式 arms。
    /// </summary>
    public SwitchStructureAnalysis Analyze(SyntaxNode root, CpgAnalysisContext context)
    {
        _ = context;

        var structure = root switch
        {
            SwitchStatementSyntax switchStatement => AnalyzeSwitchStatement(switchStatement),
            SwitchExpressionSyntax switchExpression => AnalyzeSwitchExpression(switchExpression),
            _ => throw new ArgumentException("Switch root must be switch statement or switch expression.", nameof(root))
        };

        return new SwitchStructureAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, structure.Members));
    }

    private static SwitchStructure AnalyzeSwitchStatement(SwitchStatementSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.Expression };

        foreach (var section in root.Sections)
        {
            nodes.Add(section);
            nodes.AddRange(section.Labels);
            nodes.AddRange(section.Statements);
        }

        return new SwitchStructure(root, nodes);
    }

    private static SwitchStructure AnalyzeSwitchExpression(SwitchExpressionSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.GoverningExpression };

        foreach (var arm in root.Arms)
        {
            nodes.Add(arm);
            nodes.Add(arm.Pattern);
            AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, arm.WhenClause);
            nodes.Add(arm.Expression);
        }

        return new SwitchStructure(root, nodes);
    }
}
