using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuikGraph;

namespace TerrariaTools.Analysis.Dome
{
    /// <summary>
    /// 数据流依赖图节点类型。
    /// </summary>
    public enum DataFlowDependencyNodeKind
    {
        /// <summary>
        /// 语句节点（例如：赋值、返回、表达式语句）。
        /// </summary>
        Statement,
        /// <summary>
        /// 变量/符号节点（例如：局部变量、参数）。
        /// </summary>
        Variable
    }

    /// <summary>
    /// 数据流依赖图边类型。
    /// </summary>
    public enum DataFlowDependencyEdgeKind
    {
        /// <summary>
        /// 定义关系：语句 -> 变量 (例如：int a = 1; 语句定义变量 a)。
        /// </summary>
        Defines,
        /// <summary>
        /// 使用关系：变量 -> 语句 (例如：return a; 语句使用变量 a)。
        /// </summary>
        Uses,
        /// <summary>
        /// 控制流关系：语句 -> 语句 (例如：s1 之后紧接着执行 s2)。
        /// </summary>
        Precedes
    }

    /// <summary>
    /// 表示数据流依赖图中的一个节点。
    /// 可以是一个语句，也可以是一个变量符号。
    /// </summary>
    public sealed class DataFlowDependencyNode
    {
        public string Id { get; }
        public SyntaxNode Syntax { get; }
        public ISymbol Symbol { get; }
        public DataFlowDependencyNodeKind Kind { get; }
        public string DisplayName { get; }

        public DataFlowDependencyNode(string id, SyntaxNode syntax, DataFlowDependencyNodeKind kind, string displayName, ISymbol symbol = null)
        {
            Id = id;
            Syntax = syntax;
            Kind = kind;
            DisplayName = displayName;
            Symbol = symbol;
        }

        public override string ToString() => $"[{Kind}] {DisplayName}";
    }

    /// <summary>
    /// 表示数据流依赖图中的一条边。
    /// </summary>
    public sealed class DataFlowDependencyEdge : Edge<DataFlowDependencyNode>
    {
        public DataFlowDependencyEdgeKind Kind { get; }
        public string Label { get; }

        public DataFlowDependencyEdge(DataFlowDependencyNode source, DataFlowDependencyNode target, DataFlowDependencyEdgeKind kind, string label = "")
            : base(source, target)
        {
            Kind = kind;
            Label = label;
        }

        public override string ToString() => $"({Source.DisplayName}) --[{Kind}:{Label}]--> ({Target.DisplayName})";
    }

    /// <summary>
    /// 全上下文模式的数据流依赖图实现。
    /// 包含语句节点、变量节点、定义边、使用边和控制流边。
    /// </summary>
    public sealed class DataFlowDependencyGraph
    {
        private readonly BidirectionalGraph<DataFlowDependencyNode, DataFlowDependencyEdge> _graph = new();
        private readonly Dictionary<SyntaxNode, DataFlowDependencyNode> _syntaxToNode = new();
        private readonly Dictionary<ISymbol, DataFlowDependencyNode> _symbolToNode = new(SymbolEqualityComparer.Default);

        public IEnumerable<DataFlowDependencyNode> Nodes => _graph.Vertices;
        public IEnumerable<DataFlowDependencyEdge> Edges => _graph.Edges;

        public IEnumerable<DataFlowDependencyEdge> InEdges(DataFlowDependencyNode node) => _graph.InEdges(node);
        public IEnumerable<DataFlowDependencyEdge> OutEdges(DataFlowDependencyNode node) => _graph.OutEdges(node);

        public void AddNode(DataFlowDependencyNode node)
        {
            _graph.AddVertex(node);
            _syntaxToNode[node.Syntax] = node;
            if (node.Symbol != null)
            {
                _symbolToNode[node.Symbol] = node;
            }
        }

        public void AddEdge(DataFlowDependencyEdge edge)
        {
            _graph.AddEdge(edge);
        }

        public DataFlowDependencyNode GetNode(SyntaxNode syntax) => _syntaxToNode.TryGetValue(syntax, out var node) ? node : null;
        public DataFlowDependencyNode GetNode(ISymbol symbol) => _symbolToNode.TryGetValue(symbol, out var node) ? node : null;
    }

    /// <summary>
    /// 全上下文数据流依赖分析器。
    /// 基于语法分析构建包含语句、变量及其相互依赖关系的图。
    /// </summary>
    public sealed class DataFlowDependencyAnalyzer : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly DataFlowDependencyGraph _graph = new();
        private SyntaxNode _previousStatement;

        public DataFlowDependencyAnalyzer(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public DataFlowDependencyGraph Graph => _graph;

        public void Analyze(SyntaxNode node)
        {
            Reset();
            Visit(node);
        }

        public void Reset()
        {
            _previousStatement = null;
        }

        public override void VisitBlock(BlockSyntax node)
        {
            var lastStatement = _previousStatement;
            _previousStatement = null;
            base.VisitBlock(node);
            _previousStatement = lastStatement;
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var varNode = EnsureVariableNode(symbol, node);
                var statement = node.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
                if (statement != null)
                {
                    var stmtNode = EnsureStatementNode(statement);
                    _graph.AddEdge(new DataFlowDependencyEdge(stmtNode, varNode, DataFlowDependencyEdgeKind.Defines, symbol.Name));
                }
            }
            base.VisitVariableDeclarator(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            // 处理复杂左值赋值情况
            var leftSymbols = ExtractLValueSymbols(node.Left);
            var statement = node.Ancestors().OfType<StatementSyntax>().FirstOrDefault();

            if (statement != null)
            {
                var stmtNode = EnsureStatementNode(statement);
                foreach (var symbol in leftSymbols)
                {
                    var varNode = EnsureVariableNode(symbol, node.Left);
                    _graph.AddEdge(new DataFlowDependencyEdge(stmtNode, varNode, DataFlowDependencyEdgeKind.Defines, symbol.Name));
                }
            }

            base.VisitAssignmentExpression(node);
        }

        private IEnumerable<ISymbol> ExtractLValueSymbols(ExpressionSyntax expression)
        {
            // 直接符号
            var symbolInfo = _semanticModel.GetSymbolInfo(expression);
            if (symbolInfo.Symbol != null)
            {
                yield return symbolInfo.Symbol;
            }
            else
            {
                // 处理特殊情况，如数组访问 a[i]
                if (expression is ElementAccessExpressionSyntax elementAccess)
                {
                    foreach (var sym in ExtractLValueSymbols(elementAccess.Expression))
                    {
                        yield return sym;
                    }
                }
                // 处理成员访问 p.Name
                else if (expression is MemberAccessExpressionSyntax memberAccess)
                {
                    foreach (var sym in ExtractLValueSymbols(memberAccess.Expression))
                    {
                        yield return sym;
                    }
                }
                // 处理元组解构 (a, b) = ...
                else if (expression is TupleExpressionSyntax tuple)
                {
                    foreach (var element in tuple.Arguments)
                    {
                        foreach (var sym in ExtractLValueSymbols(element.Expression))
                        {
                            yield return sym;
                        }
                    }
                }
            }
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!IsWriteTargetIdentifier(node))
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol;
                if (symbol != null && (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter || symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property))
                {
                    var varNode = EnsureVariableNode(symbol, node);
                    var statement = node.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
                    if (statement != null)
                    {
                        var stmtNode = EnsureStatementNode(statement);
                        _graph.AddEdge(new DataFlowDependencyEdge(varNode, stmtNode, DataFlowDependencyEdgeKind.Uses, symbol.Name));
                    }
                }
            }
            base.VisitIdentifierName(node);
        }

        private static bool IsWriteTargetIdentifier(IdentifierNameSyntax node)
        {
            if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node)
            {
                return true;
            }

            if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name == node &&
                memberAccess.Parent is AssignmentExpressionSyntax memberAssignment &&
                memberAssignment.Left == memberAccess)
            {
                return true;
            }

            return false;
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var stmtNode = EnsureStatementNode(node); // Treating field decl as a "statement" for simplicity in graph
            foreach (var variable in node.Declaration.Variables)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(variable);
                if (symbol != null)
                {
                    var varNode = EnsureVariableNode(symbol, variable);
                    _graph.AddEdge(new DataFlowDependencyEdge(stmtNode, varNode, DataFlowDependencyEdgeKind.Defines, symbol.Name));
                }
            }
            base.VisitFieldDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                var stmtNode = EnsureStatementNode(node);
                var varNode = EnsureVariableNode(symbol, node);
                _graph.AddEdge(new DataFlowDependencyEdge(stmtNode, varNode, DataFlowDependencyEdgeKind.Defines, symbol.Name));
            }
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node) { ProcessStatement(node); base.VisitExpressionStatement(node); }
        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) { ProcessStatement(node); base.VisitLocalDeclarationStatement(node); }
        public override void VisitReturnStatement(ReturnStatementSyntax node) { ProcessStatement(node); base.VisitReturnStatement(node); }

        private void ProcessStatement(SyntaxNode node)
        {
            var stmtNode = EnsureStatementNode(node);
            if (_previousStatement != null)
            {
                var prevNode = EnsureStatementNode(_previousStatement);
                _graph.AddEdge(new DataFlowDependencyEdge(prevNode, stmtNode, DataFlowDependencyEdgeKind.Precedes, "next"));
            }
            _previousStatement = node;
        }

        private DataFlowDependencyNode EnsureStatementNode(SyntaxNode syntax)
        {
            var node = _graph.GetNode(syntax);
            if (node == null)
            {
                // 使用语法树的文件路径和 Span 来创建确定性 ID
                var filePath = syntax.SyntaxTree.FilePath ?? "Memory";
                var id = $"stmt_{filePath}_{syntax.Span.Start}_{syntax.Span.Length}";

                // 限制显示名称长度，避免过长
                var display = syntax.ToString().Trim().Split('\n').First();
                if (display.Length > 50) display = display.Substring(0, 47) + "...";

                node = new DataFlowDependencyNode(id, syntax, DataFlowDependencyNodeKind.Statement, display, null);
                _graph.AddNode(node);
            }
            return node;
        }

        private DataFlowDependencyNode EnsureVariableNode(ISymbol symbol, SyntaxNode syntax)
        {
            var node = _graph.GetNode(symbol);
            if (node == null)
            {
                // 使用符号的完整显示名称作为 ID
                var id = $"var_{symbol.ToDisplayString()}";
                node = new DataFlowDependencyNode(id, syntax, DataFlowDependencyNodeKind.Variable, symbol.Name, symbol);
                _graph.AddNode(node);
            }
            return node;
        }
    }
}
