using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 定义结构分析结果。
/// </summary>
public sealed record DefinitionStructureAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// <summary>
/// 分析常见定义结构，包括变量、参数、字段、属性、方法、构造函数和类型定义。
/// </summary>
public sealed class DefinitionStructureAnalyzer
{
    /// <summary>
    /// 返回定义节点及其关键组成部分，例如类型、参数列表、继承列表、成员和初始化器。
    /// </summary>
    public DefinitionStructureAnalysis Analyze(SyntaxNode root, CpgAnalysisContext context)
    {
        var affectedNodes = root switch
        {
            VariableDeclarationSyntax declaration => AnalyzeVariableDeclaration(declaration),
            VariableDeclaratorSyntax declarator => AnalyzeVariableDeclarator(declarator),
            ParameterSyntax parameter => AnalyzeParameter(parameter),
            FieldDeclarationSyntax field => AnalyzeField(field),
            PropertyDeclarationSyntax property => AnalyzeProperty(property),
            MethodDeclarationSyntax method => AnalyzeMethod(method),
            ConstructorDeclarationSyntax constructor => AnalyzeConstructor(constructor),
            ClassDeclarationSyntax type => AnalyzeClass(type),
            StructDeclarationSyntax type => AnalyzeStruct(type),
            InterfaceDeclarationSyntax type => AnalyzeInterface(type),
            RecordDeclarationSyntax type => AnalyzeRecord(type),
            _ => throw new ArgumentException("Unsupported definition syntax node.", nameof(root))
        };

        return new DefinitionStructureAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(root, affectedNodes));
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeVariableDeclaration(VariableDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.Type };
        nodes.AddRange(root.Variables);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeVariableDeclarator(VariableDeclaratorSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ArgumentList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeParameter(ParameterSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Type);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Default);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeField(FieldDeclarationSyntax root)
    {
        return new SyntaxNode[] { root, root.Declaration };
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeProperty(PropertyDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.Type };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.AccessorList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ExpressionBody);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeMethod(MethodDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.ReturnType, root.ParameterList };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.TypeParameterList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ConstraintClauses.FirstOrDefault());
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Body);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ExpressionBody);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeConstructor(ConstructorDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.ParameterList };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Body);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ExpressionBody);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeClass(ClassDeclarationSyntax root)
    {
        return AnalyzeTypeDeclaration(root);
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeStruct(StructDeclarationSyntax root)
    {
        return AnalyzeTypeDeclaration(root);
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeInterface(InterfaceDeclarationSyntax root)
    {
        return AnalyzeTypeDeclaration(root);
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeRecord(RecordDeclarationSyntax root)
    {
        var nodes = AnalyzeTypeDeclaration(root).ToList();
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ParameterList);
        return nodes;
    }

    private static IReadOnlyList<SyntaxNode> AnalyzeTypeDeclaration(TypeDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.TypeParameterList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.BaseList);
        nodes.AddRange(root.Members);
        return nodes;
    }
}
