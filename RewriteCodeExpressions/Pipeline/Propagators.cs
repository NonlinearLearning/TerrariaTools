using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTools.RewriteCodeExpressions.Pipeline
{
    /// <summary>
    /// 结构传播收集器：负责自底向上将标记从子节点传播到父节点。
    /// </summary>
    public class UpwardMarkCollector : CSharpSyntaxWalker
    {
        private readonly HashSet<SyntaxNode> _nodesToMark;

        /// <summary>
        /// 初始化 UpwardMarkCollector 的新实例。
        /// </summary>
        /// <param name="nodesToMark">要标记的节点集合。</param>
        public UpwardMarkCollector(HashSet<SyntaxNode> nodesToMark)
        {
            _nodesToMark = nodesToMark;
        }

        /// <summary>
        /// 访问语法节点并执行传播逻辑。
        /// </summary>
        public override void Visit(SyntaxNode? node)
        {
            if (node == null) return;
            base.Visit(node);
        }

        /// <summary>
        /// 处理特性语法。
        /// </summary>
        public override void VisitAttribute(AttributeSyntax node)
        {
            base.VisitAttribute(node);
            // 如果特性的名称被标记（通常是因为其对应的类型被标记），则标记整个特性。
            if (_nodesToMark.Contains(node.Name))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理特性列表语法。
        /// </summary>
        public override void VisitAttributeList(AttributeListSyntax node)
        {
            base.VisitAttributeList(node);
            // 如果特性列表中有任何一个特性被标记，则标记整个列表。
            if (node.Attributes.Any(a => _nodesToMark.Contains(a)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理表达式语句。
        /// </summary>
        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            base.VisitExpressionStatement(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 With 表达式。
        /// </summary>
        public override void VisitWithExpression(WithExpressionSyntax node)
        {
            base.VisitWithExpression(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理对象创建表达式。
        /// </summary>
        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            base.VisitObjectCreationExpression(node);
            if ((node.ArgumentList != null && _nodesToMark.Contains(node.ArgumentList)) ||
                (node.Initializer != null && _nodesToMark.Contains(node.Initializer)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理初始化器表达式。
        /// </summary>
        public override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            base.VisitInitializerExpression(node);
            if (node.Expressions.Any(e => _nodesToMark.Contains(e)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理赋值表达式。
        /// </summary>
        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            base.VisitAssignmentExpression(node);
            if (_nodesToMark.Contains(node.Left) || _nodesToMark.Contains(node.Right))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理局部变量声明语句。
        /// </summary>
        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            base.VisitLocalDeclarationStatement(node);
            // 如果所有变量声明都被标记移除，则整个声明语句也标记移除。
            if (_nodesToMark.Contains(node.Declaration) || node.Declaration.Variables.All(v => _nodesToMark.Contains(v)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理变量声明。
        /// </summary>
        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            base.VisitVariableDeclaration(node);
            if (_nodesToMark.Contains(node.Type) || node.Variables.All(v => _nodesToMark.Contains(v)))
            {
                _nodesToMark.Add(node);
                foreach (var v in node.Variables) _nodesToMark.Add(v);
            }
        }

        /// <summary>
        /// 处理字段声明。
        /// </summary>
        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            base.VisitFieldDeclaration(node);
            if (_nodesToMark.Contains(node.Declaration))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Yield 语句。
        /// </summary>
        public override void VisitYieldStatement(YieldStatementSyntax node)
        {
            base.VisitYieldStatement(node);
            if (node.Expression != null && _nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Throw 语句。
        /// </summary>
        public override void VisitThrowStatement(ThrowStatementSyntax node)
        {
            base.VisitThrowStatement(node);
            if (node.Expression != null && _nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理箭头表达式子句。
        /// </summary>
        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            base.VisitArrowExpressionClause(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Switch 语句。
        /// </summary>
        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            base.VisitSwitchStatement(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 If 语句。
        /// </summary>
        public override void VisitIfStatement(IfStatementSyntax node)
        {
            base.VisitIfStatement(node);
            if (_nodesToMark.Contains(node.Condition))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 While 语句。
        /// </summary>
        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            base.VisitWhileStatement(node);
            if (_nodesToMark.Contains(node.Condition))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Do 语句。
        /// </summary>
        public override void VisitDoStatement(DoStatementSyntax node)
        {
            base.VisitDoStatement(node);
            if (_nodesToMark.Contains(node.Condition))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Lock 语句。
        /// </summary>
        public override void VisitLockStatement(LockStatementSyntax node)
        {
            base.VisitLockStatement(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Using 语句。
        /// </summary>
        public override void VisitUsingStatement(UsingStatementSyntax node)
        {
            base.VisitUsingStatement(node);
            if ((node.Expression != null && _nodesToMark.Contains(node.Expression)) ||
                (node.Declaration != null && _nodesToMark.Contains(node.Declaration)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Fixed 语句。
        /// </summary>
        public override void VisitFixedStatement(FixedStatementSyntax node)
        {
            base.VisitFixedStatement(node);
            if (_nodesToMark.Contains(node.Declaration))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 ForEach 语句。
        /// </summary>
        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            base.VisitForEachStatement(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理方法声明。
        /// </summary>
        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            base.VisitMethodDeclaration(node);
            if (_nodesToMark.Contains(node.ReturnType) || (node.ExpressionBody != null && _nodesToMark.Contains(node.ExpressionBody)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理参数。
        /// </summary>
        public override void VisitParameter(ParameterSyntax node)
        {
            base.VisitParameter(node);
            if (node.Type != null && _nodesToMark.Contains(node.Type))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理属性声明。
        /// </summary>
        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            base.VisitPropertyDeclaration(node);
            if (_nodesToMark.Contains(node.Type) || (node.ExpressionBody != null && _nodesToMark.Contains(node.ExpressionBody)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理索引器声明。
        /// </summary>
        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            base.VisitIndexerDeclaration(node);
            if (_nodesToMark.Contains(node.Type) || (node.ExpressionBody != null && _nodesToMark.Contains(node.ExpressionBody)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理事件声明。
        /// </summary>
        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            base.VisitEventDeclaration(node);
            if (_nodesToMark.Contains(node.Type))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理委托声明。
        /// </summary>
        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            base.VisitDelegateDeclaration(node);
            if (_nodesToMark.Contains(node.ReturnType))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理变量声明器。
        /// </summary>
        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            base.VisitVariableDeclarator(node);
            if (node.Initializer != null && _nodesToMark.Contains(node.Initializer.Value))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理参数。
        /// </summary>
        public override void VisitArgument(ArgumentSyntax node)
        {
            base.VisitArgument(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理参数列表。
        /// </summary>
        public override void VisitArgumentList(ArgumentListSyntax node)
        {
            base.VisitArgumentList(node);
            if (node.Arguments.Any(a => _nodesToMark.Contains(a)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理带方括号的参数列表。
        /// </summary>
        public override void VisitBracketedArgumentList(BracketedArgumentListSyntax node)
        {
            base.VisitBracketedArgumentList(node);
            if (node.Arguments.Any(a => _nodesToMark.Contains(a)))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理元素访问表达式。
        /// </summary>
        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            base.VisitElementAccessExpression(node);
            if (_nodesToMark.Contains(node.Expression) || _nodesToMark.Contains(node.ArgumentList))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理成员访问表达式。
        /// </summary>
        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            base.VisitMemberAccessExpression(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理调用表达式。
        /// </summary>
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            base.VisitInvocationExpression(node);
            if (_nodesToMark.Contains(node.Expression) || _nodesToMark.Contains(node.ArgumentList))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理带括号的表达式。
        /// </summary>
        public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            base.VisitParenthesizedExpression(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理前置一元表达式。
        /// </summary>
        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            base.VisitPrefixUnaryExpression(node);
            if (_nodesToMark.Contains(node.Operand))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理后置一元表达式。
        /// </summary>
        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            base.VisitPostfixUnaryExpression(node);
            if (_nodesToMark.Contains(node.Operand))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Switch 表达式。
        /// </summary>
        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            base.VisitSwitchExpression(node);
            if (_nodesToMark.Contains(node.GoverningExpression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Switch 表达式分支。
        /// </summary>
        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            base.VisitSwitchExpressionArm(node);
            if (_nodesToMark.Contains(node.Expression) || _nodesToMark.Contains(node.Pattern))
            {
                _nodesToMark.Add(node);
            }
            if (node.WhenClause != null && _nodesToMark.Contains(node.WhenClause.Condition))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理声明模式。
        /// </summary>
        public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
        {
            base.VisitDeclarationPattern(node);
            if (_nodesToMark.Contains(node.Designation) || _nodesToMark.Contains(node.Type))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Is 模式表达式。
        /// </summary>
        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            base.VisitIsPatternExpression(node);
            if (_nodesToMark.Contains(node.Pattern) || _nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Var 模式。
        /// </summary>
        public override void VisitVarPattern(VarPatternSyntax node)
        {
            base.VisitVarPattern(node);
            if (_nodesToMark.Contains(node.Designation))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理递归模式。
        /// </summary>
        public override void VisitRecursivePattern(RecursivePatternSyntax node)
        {
            base.VisitRecursivePattern(node);
            if (node.Designation != null && _nodesToMark.Contains(node.Designation))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Await 表达式。
        /// </summary>
        public override void VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            base.VisitAwaitExpression(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Checked 表达式。
        /// </summary>
        public override void VisitCheckedExpression(CheckedExpressionSyntax node)
        {
            base.VisitCheckedExpression(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理 Ref 表达式。
        /// </summary>
        public override void VisitRefExpression(RefExpressionSyntax node)
        {
            base.VisitRefExpression(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理二元表达式并应用传播规则。
        /// </summary>
        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            base.VisitBinaryExpression(node);

            // 对于算术运算符，仅当两侧都被标记时才标记整个表达式。
            bool isArithmetic = node.IsKind(SyntaxKind.AddExpression) ||
                                node.IsKind(SyntaxKind.SubtractExpression) ||
                                node.IsKind(SyntaxKind.MultiplyExpression) ||
                                node.IsKind(SyntaxKind.DivideExpression) ||
                                node.IsKind(SyntaxKind.ModuloExpression) ||
                                node.IsKind(SyntaxKind.LeftShiftExpression) ||
                                node.IsKind(SyntaxKind.RightShiftExpression) ||
                                node.IsKind(SyntaxKind.BitwiseAndExpression) ||
                                node.IsKind(SyntaxKind.BitwiseOrExpression) ||
                                node.IsKind(SyntaxKind.ExclusiveOrExpression);

            if (node.Kind() == SyntaxKind.LogicalAndExpression || node.Kind() == SyntaxKind.LogicalOrExpression || isArithmetic)
            {
                if (_nodesToMark.Contains(node.Left) && _nodesToMark.Contains(node.Right))
                {
                    _nodesToMark.Add(node);
                }
            }
            // 对于其他二元表达式（如比较、As/Is 等），只要有一个操作数被标记，整个表达式就应该被标记。
            else if (_nodesToMark.Contains(node.Left) || _nodesToMark.Contains(node.Right))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理条件（三元）表达式。
        /// </summary>
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            base.VisitConditionalExpression(node);
            if (_nodesToMark.Contains(node.Condition) || _nodesToMark.Contains(node.WhenTrue) || _nodesToMark.Contains(node.WhenFalse))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理内插字符串表达式。
        /// </summary>
        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            base.VisitInterpolatedStringExpression(node);
            foreach (var content in node.Contents)
            {
                if (_nodesToMark.Contains(content))
                {
                    _nodesToMark.Add(node);
                    break;
                }
            }
        }

        /// <summary>
        /// 处理内插表达式。
        /// </summary>
        public override void VisitInterpolation(InterpolationSyntax node)
        {
            base.VisitInterpolation(node);
            if (_nodesToMark.Contains(node.Expression))
            {
                _nodesToMark.Add(node);
            }
        }

        /// <summary>
        /// 处理强制转换表达式。
        /// </summary>
        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            base.VisitCastExpression(node);
            if (_nodesToMark.Contains(node.Expression) || _nodesToMark.Contains(node.Type))
            {
                _nodesToMark.Add(node);
            }
        }
    }

    /// <summary>
    /// 动态模型传播器：保留用于向后兼容或特定的动态场景。
    /// </summary>
    public class DynamicModelPropagator : NodeTrackingPropagator
    {
        /// <summary>
        /// 初始化 DynamicModelPropagator 的新实例。
        /// </summary>
        public DynamicModelPropagator(SemanticModel model, HashSet<SyntaxNode> nodesToMark) : base(model, nodesToMark) { }
    }

    /// <summary>
    /// 生命周期托管传播器：保留用于向后兼容或特定的生命周期场景。
    /// </summary>
    public class LifecycleManagedPropagator : NodeTrackingPropagator
    {
        /// <summary>
        /// 初始化 LifecycleManagedPropagator 的新实例。
        /// </summary>
        public LifecycleManagedPropagator(SemanticModel model, HashSet<SyntaxNode> nodesToMark) : base(model, nodesToMark) { }
    }

    /// <summary>
    /// 语义传播器基类：负责基于符号引用在语法树中传播标记。
    /// </summary>
    public class NodeTrackingPropagator : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly HashSet<SyntaxNode> _nodesToMark;

        /// <summary>
        /// 初始化 NodeTrackingPropagator 的新实例。
        /// </summary>
        public NodeTrackingPropagator(SemanticModel model, HashSet<SyntaxNode> nodesToMark)
        {
            _model = model;
            _nodesToMark = nodesToMark;
        }

        /// <summary>
        /// 处理标识符名称。
        /// </summary>
        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol != null && IsMarked(symbol))
            {
                _nodesToMark.Add(node);
            }
            base.VisitIdentifierName(node);
        }

        /// <summary>
        /// 处理 Base 表达式。
        /// </summary>
        public override void VisitBaseExpression(BaseExpressionSyntax node)
        {
            var type = _model.GetTypeInfo(node).Type;
            if (type != null && IsMarked(type))
            {
                _nodesToMark.Add(node);
            }
            base.VisitBaseExpression(node);
        }

        /// <summary>
        /// 处理 This 表达式。
        /// </summary>
        public override void VisitThisExpression(ThisExpressionSyntax node)
        {
            var type = _model.GetTypeInfo(node).Type;
            if (type != null && IsMarked(type))
            {
                _nodesToMark.Add(node);
            }
            base.VisitThisExpression(node);
        }

        /// <summary>
        /// 检查指定符号是否已被标记。
        /// </summary>
        private bool IsMarked(ISymbol symbol)
        {
            if (symbol == null) return false;

            foreach (var node in _nodesToMark)
            {
                ISymbol? markedSymbol = null;
                switch (node)
                {
                    case VariableDeclaratorSyntax v:
                        markedSymbol = _model.GetDeclaredSymbol(v);
                        break;
                    case ParameterSyntax p:
                        markedSymbol = _model.GetDeclaredSymbol(p);
                        break;
                    case PropertyDeclarationSyntax prop:
                        markedSymbol = _model.GetDeclaredSymbol(prop);
                        break;
                    case MethodDeclarationSyntax m:
                        markedSymbol = _model.GetDeclaredSymbol(m);
                        break;
                    case ClassDeclarationSyntax c:
                        markedSymbol = _model.GetDeclaredSymbol(c);
                        break;
                    case InterfaceDeclarationSyntax i:
                        markedSymbol = _model.GetDeclaredSymbol(i);
                        break;
                    case EnumDeclarationSyntax e:
                        markedSymbol = _model.GetDeclaredSymbol(e);
                        break;
                    case TypeParameterSyntax tp:
                        markedSymbol = _model.GetDeclaredSymbol(tp);
                        break;
                    case UsingDirectiveSyntax u:
                        markedSymbol = _model.GetDeclaredSymbol(u);
                        break;
                    case SingleVariableDesignationSyntax sv:
                        markedSymbol = _model.GetDeclaredSymbol(sv);
                        break;
                    case CatchDeclarationSyntax cd:
                        markedSymbol = _model.GetDeclaredSymbol(cd);
                        break;
                    case ForEachStatementSyntax fe:
                        markedSymbol = _model.GetDeclaredSymbol(fe);
                        break;
                    case IdentifierNameSyntax id:
                        var symbolInfo = _model.GetSymbolInfo(id);
                        markedSymbol = symbolInfo.Symbol;
                        break;
                }

                if (markedSymbol != null && SymbolEqualityComparer.Default.Equals(symbol, markedSymbol))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 预处理符号传播器：负责批量同步符号引用状态。
    /// </summary>
    public class PreprocessedSymbolPropagator
    {
        private readonly SemanticModel _model;
        private readonly HashSet<SyntaxNode> _nodesToMark;
        private readonly SyntaxNode _root;

        /// <summary>
        /// 初始化 PreprocessedSymbolPropagator 的新实例。
        /// </summary>
        public PreprocessedSymbolPropagator(SemanticModel model, HashSet<SyntaxNode> nodesToMark, SyntaxNode root)
        {
            _model = model;
            _nodesToMark = nodesToMark;
            _root = root;
        }

        /// <summary>
        /// 执行标记传播。
        /// </summary>
        public void Propagate()
        {
            var markedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var node in _nodesToMark)
            {
                ISymbol? symbol = null;
                switch (node)
                {
                    case VariableDeclaratorSyntax v:
                        symbol = _model.GetDeclaredSymbol(v);
                        break;
                    case ParameterSyntax p:
                        symbol = _model.GetDeclaredSymbol(p);
                        break;
                    case PropertyDeclarationSyntax prop:
                        symbol = _model.GetDeclaredSymbol(prop);
                        break;
                    case MethodDeclarationSyntax m:
                        symbol = _model.GetDeclaredSymbol(m);
                        break;
                    case ClassDeclarationSyntax c:
                        symbol = _model.GetDeclaredSymbol(c);
                        break;
                    case InterfaceDeclarationSyntax i:
                        symbol = _model.GetDeclaredSymbol(i);
                        break;
                    case EnumDeclarationSyntax e:
                        symbol = _model.GetDeclaredSymbol(e);
                        break;
                    case TypeParameterSyntax tp:
                        symbol = _model.GetDeclaredSymbol(tp);
                        break;
                    case UsingDirectiveSyntax u:
                        symbol = _model.GetDeclaredSymbol(u);
                        break;
                    case SingleVariableDesignationSyntax sv:
                        symbol = _model.GetDeclaredSymbol(sv);
                        break;
                    case CatchDeclarationSyntax cd:
                        symbol = _model.GetDeclaredSymbol(cd);
                        break;
                    case ForEachStatementSyntax fe:
                        symbol = _model.GetDeclaredSymbol(fe);
                        break;
                    case SimpleNameSyntax id:
                        var symbolInfo = _model.GetSymbolInfo(id);
                        symbol = symbolInfo.Symbol;
                        if (symbol == null && id.Parent is AttributeSyntax attr && attr.Name == id)
                        {
                            symbol = _model.GetSymbolInfo(attr).Symbol;
                        }
                        break;
                }

                if (symbol != null)
                {
                    markedSymbols.Add(symbol);
                    if (symbol is INamedTypeSymbol nts)
                    {
                        foreach (var ctor in nts.InstanceConstructors)
                        {
                            markedSymbols.Add(ctor);
                        }
                    }
                }
            }

            if (markedSymbols.Count == 0)
            {
                return;
            }

            foreach (var node in _root.DescendantNodes())
            {
                ISymbol? symbol = null;
                if (node is IdentifierNameSyntax identifier)
                {
                    symbol = _model.GetSymbolInfo(identifier).Symbol;
                }
                else if (node is BaseExpressionSyntax baseExpr)
                {
                    symbol = _model.GetTypeInfo(baseExpr).Type;
                }
                else if (node is ThisExpressionSyntax thisExpr)
                {
                    symbol = _model.GetTypeInfo(thisExpr).Type;
                }

                if (symbol != null && markedSymbols.Contains(symbol))
                {
                    _nodesToMark.Add(node);
                }
            }
        }
    }
}
