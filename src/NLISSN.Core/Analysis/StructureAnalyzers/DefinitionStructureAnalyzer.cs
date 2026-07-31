using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// 定义结构分析结果。
public sealed record DefinitionStructureAnalysis(IReadOnlyList<SyntaxNode> AffectedSyntaxTree);

/// 分析常见定义结构，包括变量、参数、字段、属性、方法、构造函数和类型定义。
public sealed class DefinitionStructureAnalyzer
{
    private sealed record DefinitionStructure(
        SyntaxNode Root,
        IReadOnlyList<SyntaxNode> Members);

    // 针对不同声明语法抽取定义结构节点，统一供规则阶段消费。
    public DefinitionStructureAnalysis Analyze(SyntaxNode root, CpgAnalysisContext context)
    {
        _ = context;

        var structure = root switch
        {
            VariableDeclarationSyntax declaration => AnalyzeVariableDeclaration(declaration),
            VariableDeclaratorSyntax declarator => AnalyzeVariableDeclarator(declarator),
            ParameterSyntax parameter => AnalyzeParameter(parameter),
            FieldDeclarationSyntax field => AnalyzeField(field),
            PropertyDeclarationSyntax property => AnalyzeProperty(property),
            MethodDeclarationSyntax method => AnalyzeMethod(method),
            ConstructorDeclarationSyntax constructor => AnalyzeConstructor(constructor),
            ClassDeclarationSyntax type => AnalyzeTypeDeclaration(type),
            StructDeclarationSyntax type => AnalyzeTypeDeclaration(type),
            InterfaceDeclarationSyntax type => AnalyzeTypeDeclaration(type),
            RecordDeclarationSyntax type => AnalyzeRecord(type),
            _ => throw new ArgumentException("Unsupported definition syntax node.", nameof(root))
        };

        return new DefinitionStructureAnalysis(
            AnalysisSyntaxNodeCollector.BuildAffectedSyntaxTree(structure.Root, structure.Members));
    }

    private static DefinitionStructure AnalyzeVariableDeclaration(VariableDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.Type };
        nodes.AddRange(root.Variables);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeVariableDeclarator(VariableDeclaratorSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ArgumentList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeParameter(ParameterSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Type);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Default);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeField(FieldDeclarationSyntax root)
    {
        return new DefinitionStructure(root, new SyntaxNode[] { root, root.Declaration });
    }

    private static DefinitionStructure AnalyzeProperty(PropertyDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.Type };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.AccessorList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ExpressionBody);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeMethod(MethodDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.ReturnType, root.ParameterList };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.TypeParameterList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ConstraintClauses.FirstOrDefault());
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Body);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ExpressionBody);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeConstructor(ConstructorDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root, root.ParameterList };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Initializer);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.Body);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ExpressionBody);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeRecord(RecordDeclarationSyntax root)
    {
        var nodes = AnalyzeTypeDeclaration(root).Members.ToList();
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.ParameterList);
        return new DefinitionStructure(root, nodes);
    }

    private static DefinitionStructure AnalyzeTypeDeclaration(TypeDeclarationSyntax root)
    {
        var nodes = new List<SyntaxNode> { root };
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.TypeParameterList);
        AnalysisSyntaxNodeCollector.AddIfNotNull(nodes, root.BaseList);
        nodes.AddRange(root.Members);
        return new DefinitionStructure(root, nodes);
    }
}
