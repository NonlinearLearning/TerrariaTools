/**
 * 功能描述：提供结构性标记收集逻辑，负责根据 C# 语法结构将待移除标记向上（父节点）传播。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 结构性标记收集器，继承自 CSharpSyntaxWalker。
    /// 该类的职责是根据 C# 的语法结构逻辑，将"待移除"状态从子节点向上（父节点）传播。
    /// 它通过识别特定的语法模式（如：如果初始值被移除，则变量声明也应移除），
    /// 确保语法树清理的连贯性和完整性，避免留下孤立或无效的语法片段。
    /// </summary>
    public class UpwardMarkCollector : CSharpSyntaxWalker
    {
        /// <summary>
        /// 存储所有被标记为"待移除"的语法节点集合。
        /// </summary>
        private readonly System.Collections.Generic.HashSet<SyntaxNode> NodesToMark;

        /// <summary>
        /// 限制向上传播的根节点。传播不会超出此节点。
        /// </summary>
        private readonly SyntaxNode? LimitNode;

        /// <summary>
        /// 初始化 UpwardMarkCollector 的新实例。
        /// </summary>
        /// <param name="NodesToMark">初始的待标记节点集合</param>
        /// <param name="LimitNode">可选的限制范围根节点</param>
        public UpwardMarkCollector(System.Collections.Generic.HashSet<SyntaxNode> NodesToMark, SyntaxNode? LimitNode = null)
        {
            this.NodesToMark = NodesToMark;
            this.LimitNode = LimitNode;
        }

        /// <summary>
        /// 通用的访问方法。
        /// 逻辑：首先递归访问子节点。访问完子节点后，如果发现当前节点的所有子节点（不含 Token）
        /// 都已被标记为移除，则自动将当前节点也标记为移除。
        /// </summary>
        /// <param name="Node">当前访问的节点</param>
        public override void Visit(SyntaxNode? Node)
        {
            if (Node == null) return;

            // 1. 递归向下访问子节点
            base.Visit(Node);

            // 2. 确定范围边界：不要标记限制根节点本身，也不要超出限制范围
            if (Node == LimitNode) return;

            // 3. 排除一些具有结构意义的节点，作为向上传播的边界。
            // 这些节点通常由 ExpressionSimplifier 处理，或者它们可以包含占位符。
            if (Node is CompilationUnitSyntax ||
                Node is NamespaceDeclarationSyntax ||
                Node is BaseTypeDeclarationSyntax || // 类、结构、接口、枚举声明
                Node is MemberDeclarationSyntax || // 方法、字段、属性等成员声明
                Node is BlockSyntax || // 语句块
                Node is YieldStatementSyntax || // yield return/break
                Node is ReturnStatementSyntax || // return
                Node is SwitchExpressionArmSyntax || // switch 分支
                Node is ExpressionSyntax || // 各种表达式
                Node is EqualsValueClauseSyntax || // 等号初始化部分
                Node is VariableDeclaratorSyntax || // 变量声明器
                Node is ParameterListSyntax || // 参数列表
                Node is TypeParameterListSyntax || // 类型参数列表
                Node is ArgumentListSyntax || // 参数列表（调用时）
                Node is AttributeArgumentListSyntax || // 特性参数列表
                Node is BracketedParameterListSyntax || // 方括号参数列表（索引器）
                Node is ArrowExpressionClauseSyntax || // 箭头表达式主体
                Node is AttributeListSyntax || // 特性列表
                Node is BaseListSyntax || // 基类列表
                Node is ConstructorInitializerSyntax || // 构造函数初始化器
                Node is AnonymousObjectMemberDeclaratorSyntax || // 匿名对象成员声明
                Node is InterpolationSyntax) // 内插字符串插值部分
            {
                return;
            }

            // 字面量不参与向上传播逻辑（它们没有子节点可以导致它们被标记）
            if (Node is LiteralExpressionSyntax) return;

            var ChildNodes = Node.ChildNodes().ToList();
            if (ChildNodes.Count > 0 && ChildNodes.All(Child => this.NodesToMark.Contains(Child)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        /// <summary>
        /// 访问变量声明。
        /// 逻辑：如果所有的变量都被标记为移除，或者类型本身被标记为移除，则标记整个声明。
        /// </summary>
        public override void VisitVariableDeclaration(VariableDeclarationSyntax Node)
        {
            base.VisitVariableDeclaration(Node);
            if (this.NodesToMark.Contains(Node.Type) || (Node.Variables.Count > 0 && Node.Variables.All(Variable => this.NodesToMark.Contains(Variable))))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax Node)
        {
            base.VisitVariableDeclarator(Node);
            // 如果初始化器被标记为移除，则整个变量声明器也应被标记为移除（除非是字段且我们想保留它）
            // 注意：ExpressionSimplifier 会根据 IsValueRequiredContext 决定是否保留占位符。
            // 如果我们在这里标记了 Node，那么 Simplifier 会直接移除它。
            if (Node.Initializer != null && this.NodesToMark.Contains(Node.Initializer))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax Node)
        {
            base.VisitEqualsValueClause(Node);
            if (this.NodesToMark.Contains(Node.Value))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitParameter(ParameterSyntax Node)
        {
            base.VisitParameter(Node);
            // 如果参数类型被标记，或者参数名被标记（通常不会），或者整个参数被标记
            if (Node.Type != null && this.NodesToMark.Contains(Node.Type))
            {
                this.NodesToMark.Add(Node);
            }
        }

        /// <summary>
        /// 访问代码块。
        /// 传播逻辑：如果所有的语句都被标记为移除，则标记整个代码块。
        /// </summary>
        public override void VisitBlock(BlockSyntax Node)
        {
            base.VisitBlock(Node);

            // 对于方法、属性访问器、匿名函数，我们通常不希望标记整个主体块为移除，
            // 因为这些结构的声明通常是受保护的，且我们希望保留其外壳。
            // 但对于局部函数（LocalFunctionStatementSyntax），我们允许标记其主体块，
            // 这样可以触发 VisitLocalFunctionStatement 将整个局部函数标记为移除。
            if (Node.Parent is not (BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or AnonymousFunctionExpressionSyntax))
            {
                if (Node.Statements.Count > 0 && Node.Statements.All(Statement => this.NodesToMark.Contains(Statement)))
                {
                    this.NodesToMark.Add(Node);
                }
            }
        }

        /// <summary>
        /// 访问局部函数声明。
        /// 逻辑：如果其主体（Block 或 ExpressionBody）被标记为移除，则整个局部函数也应被标记。
        /// </summary>
        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax Node)
        {
            base.VisitLocalFunctionStatement(Node);

            bool BodyMarked = Node.Body != null && this.NodesToMark.Contains(Node.Body);
            bool ExpressionBodyMarked = Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody);

            if (BodyMarked || ExpressionBodyMarked)
            {
                this.NodesToMark.Add(Node);
            }
        }

        /// <summary>
        /// 访问二元表达式节点。
        /// 传播逻辑：当且仅当二元表达式的左侧（Left）和右侧（Right）操作数都被标记为移除时，
        /// 整个二元表达式才会被标记为移除。
        /// </summary>
        public override void VisitBinaryExpression(BinaryExpressionSyntax Node)
        {
            base.VisitBinaryExpression(Node);
            if (this.NodesToMark.Contains(Node.Left) && this.NodesToMark.Contains(Node.Right))
            {
                this.NodesToMark.Add(Node);
            }
        }

        /// <summary>
        /// 访问方法调用表达式节点。
        /// 传播逻辑：只有当被调用的方法表达式（Expression）本身被标记为移除时，
        /// 整个方法调用才会被标记为移除。
        /// 注意：即使所有参数都被移除，也不自动移除方法调用，而是通过重写器为参数生成占位符。
        /// </summary>
        public override void VisitInvocationExpression(InvocationExpressionSyntax Node)
        {
            base.VisitInvocationExpression(Node);
            if (this.NodesToMark.Contains(Node.Expression))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax Node)
        {
            base.VisitAssignmentExpression(Node);
            // 如果赋值目标被标记，则整个赋值表达式也应标记
            if (this.NodesToMark.Contains(Node.Left))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitTryStatement(TryStatementSyntax Node)
        {
            base.VisitTryStatement(Node);
            // 如果 try 块被标记为移除，则整个 try 语句也应被标记
            if (this.NodesToMark.Contains(Node.Block))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitCatchClause(CatchClauseSyntax Node)
        {
            base.VisitCatchClause(Node);
            // 如果 catch 块被标记为移除，则整个 catch 子句也应被标记
            if (this.NodesToMark.Contains(Node.Block))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitFinallyClause(FinallyClauseSyntax Node)
        {
            base.VisitFinallyClause(Node);
            // 如果 finally 块被标记为移除，则整个 finally 子句也应被标记
            if (this.NodesToMark.Contains(Node.Block))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitIfStatement(IfStatementSyntax Node)
        {
            base.VisitIfStatement(Node);
            // 如果 if 的条件被标记，或者主体被标记且没有 else/else 也被标记，则标记整个 if
            if (this.NodesToMark.Contains(Node.Condition))
            {
                this.NodesToMark.Add(Node);
            }
            else if (this.NodesToMark.Contains(Node.Statement))
            {
                if (Node.Else == null || this.NodesToMark.Contains(Node.Else))
                {
                    this.NodesToMark.Add(Node);
                }
            }
        }

        public override void VisitElseClause(ElseClauseSyntax Node)
        {
            base.VisitElseClause(Node);
            // 如果 else 的主体被标记为移除，则标记整个 else 子句
            if (this.NodesToMark.Contains(Node.Statement))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax Node)
        {
            base.VisitTypeParameterConstraintClause(Node);
            if (Node.Constraints.Count > 0 && Node.Constraints.All(Constraint => this.NodesToMark.Contains(Constraint)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitTypeConstraint(TypeConstraintSyntax Node)
        {
            base.VisitTypeConstraint(Node);
            if (this.NodesToMark.Contains(Node.Type))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAttribute(AttributeSyntax Node)
        {
            base.VisitAttribute(Node);
            // 如果属性名称被标记，或者所有参数都被标记，则标记整个属性
            if (this.NodesToMark.Contains(Node.Name) || (Node.ArgumentList != null && this.NodesToMark.Contains(Node.ArgumentList)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAttributeArgument(AttributeArgumentSyntax Node)
        {
            base.VisitAttributeArgument(Node);
            if (this.NodesToMark.Contains(Node.Expression))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitUsingStatement(UsingStatementSyntax Node)
        {
            base.VisitUsingStatement(Node);
            // 如果语句主体被标记，或者声明被标记，或者表达式被标记，则标记整个 using
            if (this.NodesToMark.Contains(Node.Statement) ||
                (Node.Declaration != null && this.NodesToMark.Contains(Node.Declaration)) ||
                (Node.Expression != null && this.NodesToMark.Contains(Node.Expression)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitLockStatement(LockStatementSyntax Node)
        {
            base.VisitLockStatement(Node);
            // 如果语句主体被标记，或者锁定表达式被标记
            if (this.NodesToMark.Contains(Node.Statement) || this.NodesToMark.Contains(Node.Expression))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitWhileStatement(WhileStatementSyntax Node)
        {
            base.VisitWhileStatement(Node);
            if (this.NodesToMark.Contains(Node.Statement))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitDoStatement(DoStatementSyntax Node)
        {
            base.VisitDoStatement(Node);
            if (this.NodesToMark.Contains(Node.Statement))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitForStatement(ForStatementSyntax Node)
        {
            base.VisitForStatement(Node);
            if (this.NodesToMark.Contains(Node.Statement))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitForEachStatement(ForEachStatementSyntax Node)
        {
            base.VisitForEachStatement(Node);
            if (this.NodesToMark.Contains(Node.Statement))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax Node)
        {
            base.VisitSwitchStatement(Node);
            // 如果 switch 表达式被标记为移除，或者所有 section 都被标记为移除
            if (this.NodesToMark.Contains(Node.Expression) ||
                (Node.Sections.Count > 0 && Node.Sections.All(Section => this.NodesToMark.Contains(Section))))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitSwitchSection(SwitchSectionSyntax Node)
        {
            base.VisitSwitchSection(Node);
            // 如果 section 里的所有语句都被标记为移除
            if (Node.Statements.Count > 0 && Node.Statements.All(Statement => this.NodesToMark.Contains(Statement)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitFixedStatement(FixedStatementSyntax Node)
        {
            base.VisitFixedStatement(Node);
            // 如果声明被标记，或者语句主体被标记
            if ((Node.Declaration != null && this.NodesToMark.Contains(Node.Declaration)) || this.NodesToMark.Contains(Node.Statement))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitUnsafeStatement(UnsafeStatementSyntax Node)
        {
            base.VisitUnsafeStatement(Node);
            if (this.NodesToMark.Contains(Node.Block))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitCheckedStatement(CheckedStatementSyntax Node)
        {
            base.VisitCheckedStatement(Node);
            if (this.NodesToMark.Contains(Node.Block))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAttributeList(AttributeListSyntax Node)
        {
            base.VisitAttributeList(Node);
            if (Node.Attributes.Count > 0 && Node.Attributes.All(Attribute => this.NodesToMark.Contains(Attribute)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax Node)
        {
            base.VisitPropertyDeclaration(Node);
            // 如果存在访问器列表且被标记，或者没有访问器列表（且不是抽象属性），则标记属性
            if (Node.AccessorList != null && this.NodesToMark.Contains(Node.AccessorList))
            {
                this.NodesToMark.Add(Node);
            }
            // 如果主体或表达式主体被标记
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAccessorList(AccessorListSyntax Node)
        {
            base.VisitAccessorList(Node);
            // 只要访问器列表不为空，且所有访问器都被标记，则标记列表本身
            if (Node.Accessors.Count > 0 && Node.Accessors.All(Accessor => this.NodesToMark.Contains(Accessor)))
            {
                this.NodesToMark.Add(Node);
            }
            // 如果访问器列表为空（例如在标记过程中被清空），也应该被标记
            else if (Node.Accessors.Count == 0)
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAccessorDeclaration(AccessorDeclarationSyntax Node)
        {
            base.VisitAccessorDeclaration(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax Node)
        {
            base.VisitIndexerDeclaration(Node);
            if (Node.AccessorList != null && this.NodesToMark.Contains(Node.AccessorList))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax Node)
        {
            base.VisitEventDeclaration(Node);
            if (Node.AccessorList != null && this.NodesToMark.Contains(Node.AccessorList))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax Node)
        {
            base.VisitEventFieldDeclaration(Node);
            if (Node.Declaration.Variables.Count > 0 && Node.Declaration.Variables.All(Variable => this.NodesToMark.Contains(Variable)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax Node)
        {
            base.VisitEnumMemberDeclaration(Node);
            if (Node.EqualsValue != null && this.NodesToMark.Contains(Node.EqualsValue))
            {
                // 注意：这里通常不因为初始值被删就删掉整个枚举成员，除非它是自动生成的或者上下文要求。
                // 但根据测试 Test41，如果 EqualsValueClause 被标记，整个成员应被标记。
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax Node)
        {
            base.VisitEnumDeclaration(Node);
            if (Node.Members.Count > 0 && Node.Members.All(m => this.NodesToMark.Contains(m)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax Node)
        {
            base.VisitClassDeclaration(Node);
            if (Node.Members.Count > 0 && Node.Members.All(m => this.NodesToMark.Contains(m)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax Node)
        {
            base.VisitStructDeclaration(Node);
            if (Node.Members.Count > 0 && Node.Members.All(m => this.NodesToMark.Contains(m)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax Node)
        {
            base.VisitInterfaceDeclaration(Node);
            if (Node.Members.Count > 0 && Node.Members.All(m => this.NodesToMark.Contains(m)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax Node)
        {
            base.VisitRecordDeclaration(Node);
            if (Node.Members.Count > 0 && Node.Members.All(m => this.NodesToMark.Contains(m)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax Node)
        {
            base.VisitNamespaceDeclaration(Node);
            if (Node.Members.Count > 0 && Node.Members.All(m => this.NodesToMark.Contains(m)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitYieldStatement(YieldStatementSyntax Node)
        {
            base.VisitYieldStatement(Node);
            if (Node.Expression != null && this.NodesToMark.Contains(Node.Expression))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitWithExpression(WithExpressionSyntax Node)
        {
            base.VisitWithExpression(Node);
            if (this.NodesToMark.Contains(Node.Expression) || (Node.Initializer != null && this.NodesToMark.Contains(Node.Initializer)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax Node)
        {
            base.VisitAnonymousMethodExpression(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax Node)
        {
            base.VisitSimpleLambdaExpression(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax Node)
        {
            base.VisitParenthesizedLambdaExpression(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax Node)
        {
            base.VisitArrowExpressionClause(Node);
            if (this.NodesToMark.Contains(Node.Expression))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax Node)
        {
            base.VisitSwitchExpression(Node);
            // 注意：我们不再因为 GoverningExpression 被标记就标记整个 SwitchExpression，
            // 因为 ExpressionSimplifier 可以为 GoverningExpression 生成占位符。
            // 只有当所有分支都被标记且无法恢复时，才考虑标记整个 SwitchExpression。
            if (Node.Arms.Count > 0 && Node.Arms.All(a => this.NodesToMark.Contains(a)))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax Node)
        {
            base.VisitSwitchExpressionArm(Node);
            if (this.NodesToMark.Contains(Node.Expression))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax Node)
        {
            base.VisitConstructorDeclaration(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitDestructorDeclaration(DestructorDeclarationSyntax Node)
        {
            base.VisitDestructorDeclaration(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax Node)
        {
            base.VisitOperatorDeclaration(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax Node)
        {
            base.VisitConversionOperatorDeclaration(Node);
            if (Node.Body != null && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.ExpressionBody != null && this.NodesToMark.Contains(Node.ExpressionBody))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitQueryExpression(QueryExpressionSyntax Node)
        {
            base.VisitQueryExpression(Node);
            if (this.NodesToMark.Contains(Node.FromClause) && this.NodesToMark.Contains(Node.Body))
            {
                this.NodesToMark.Add(Node);
            }
        }

        public override void VisitQueryBody(QueryBodySyntax Node)
        {
            base.VisitQueryBody(Node);
            if (Node.Clauses.Count > 0 && Node.Clauses.All(c => this.NodesToMark.Contains(c)) && this.NodesToMark.Contains(Node.SelectOrGroup))
            {
                this.NodesToMark.Add(Node);
            }
            else if (Node.Clauses.Count == 0 && this.NodesToMark.Contains(Node.SelectOrGroup))
            {
                this.NodesToMark.Add(Node);
            }
        }
    }
}
