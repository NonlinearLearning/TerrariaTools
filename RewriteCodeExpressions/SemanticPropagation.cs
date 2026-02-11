/**
 * 功能描述：提供四种不同策略的变量引用传播逻辑，用于解决 Roslyn 语义模型与语法树不匹配的问题。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 语义传播器基类，提供通用的符号标记检查逻辑。
    /// </summary>
    public abstract class PropagatorBase : CSharpSyntaxWalker
    {
        /// <summary>
        /// 存储需要标记为移除的语法节点集合。
        /// </summary>
        protected readonly HashSet<SyntaxNode> NodesToMark;

        /// <summary>
        /// 初始化 <see cref="PropagatorBase"/> 类的新实例。
        /// </summary>
        /// <param name="NodesToMark">初始标记的节点集合</param>
        protected PropagatorBase(HashSet<SyntaxNode> NodesToMark)
        {
            this.NodesToMark = NodesToMark;
        }

        /// <summary>
        /// 判断给定符号的声明是否已被标记为移除。
        /// 支持跨语法树（如带注解的树与原始树）的标记检查。
        /// </summary>
        /// <param name="Symbol">待检查的符号</param>
        /// <returns>若符号声明被标记则返回 true，否则返回 false</returns>
        protected bool IsSymbolMarked(ISymbol Symbol)
        {
            if (Symbol == null) return false;
            foreach (var Reference in Symbol.DeclaringSyntaxReferences)
            {
                var Syntax = Reference.GetSyntax();

                // 1. 引用一致性检查（同树场景）
                if (Syntax.AncestorsAndSelf().Any(A => NodesToMark.Contains(A)))
                {
                    return true;
                }

                // 2. 跨树位置检查（不同树场景，如 annotatedRoot 与 originalTree）
                // 只要 NodesToMark 中存在任何节点（或其祖先）的 FullSpan 包含声明节点，即视为已标记
                var DeclaringSpan = Syntax.FullSpan;
                if (NodesToMark.Any(MarkedNode => MarkedNode.FullSpan.Contains(DeclaringSpan)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 安全获取可用于语义查询的节点。
        /// 如果当前节点不在语义模型的语法树中，尝试通过位置信息映射回原始树节点。
        /// </summary>
        /// <param name="Node">当前节点</param>
        /// <param name="Model">语义模型</param>
        /// <returns>映射后的节点，若无法映射则返回原节点</returns>
        protected SyntaxNode GetQueryableNode(SyntaxNode Node, SemanticModel Model)
        {
            if (Model == null || Node.SyntaxTree == Model.SyntaxTree) return Node;

            try
            {
                var OriginalRoot = Model.SyntaxTree.GetRoot();
                if (Node.FullSpan.End <= OriginalRoot.FullSpan.End)
                {
                    return OriginalRoot.FindNode(Node.FullSpan, getInnermostNodeForTie: true);
                }
            }
            catch { }

            return Node;
        }

        /// <summary>
        /// 访问标识符名称节点。
        /// </summary>
        /// <param name="Node">标识符名称节点</param>
        public override void VisitIdentifierName(IdentifierNameSyntax Node) { CheckAndMark(Node); base.VisitIdentifierName(Node); }

        /// <summary>
        /// 访问泛型名称节点。
        /// </summary>
        /// <param name="Node">泛型名称节点</param>
        public override void VisitGenericName(GenericNameSyntax Node) { CheckAndMark(Node); base.VisitGenericName(Node); }

        /// <summary>
        /// 访问对象创建表达式节点。
        /// </summary>
        /// <param name="Node">对象创建表达式节点</param>
        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax Node) { CheckAndMark(Node); base.VisitObjectCreationExpression(Node); }

        /// <summary>
        /// 访问成员访问表达式节点。
        /// </summary>
        /// <param name="Node">成员访问表达式节点</param>
        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax Node) { CheckAndMark(Node); base.VisitMemberAccessExpression(Node); }

        /// <summary>
        /// 访问条件访问表达式节点。
        /// </summary>
        /// <param name="Node">条件访问表达式节点</param>
        public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax Node) { CheckAndMark(Node); base.VisitConditionalAccessExpression(Node); }

        /// <summary>
        /// 访问二元表达式节点。
        /// </summary>
        /// <param name="Node">二元表达式节点</param>
        public override void VisitBinaryExpression(BinaryExpressionSyntax Node) { CheckAndMark(Node); base.VisitBinaryExpression(Node); }

        /// <summary>
        /// 访问赋值表达式节点。
        /// </summary>
        /// <param name="Node">赋值表达式节点</param>
        public override void VisitAssignmentExpression(AssignmentExpressionSyntax Node) { CheckAndMark(Node); base.VisitAssignmentExpression(Node); }

        /// <summary>
        /// 访问特性节点。
        /// </summary>
        /// <param name="Node">特性节点</param>
        public override void VisitAttribute(AttributeSyntax Node) { CheckAndMark(Node); base.VisitAttribute(Node); }

        /// <summary>
        /// 访问元素访问表达式节点。
        /// </summary>
        /// <param name="Node">元素访问表达式节点</param>
        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax Node) { CheckAndMark(Node); base.VisitElementAccessExpression(Node); }

        /// <summary>
        /// 访问强制类型转换表达式节点。
        /// </summary>
        /// <param name="Node">强制类型转换表达式节点</param>
        public override void VisitCastExpression(CastExpressionSyntax Node) { CheckAndMark(Node); base.VisitCastExpression(Node); }

        /// <summary>
        /// 访问隐式元素访问节点。
        /// </summary>
        /// <param name="Node">隐式元素访问节点</param>
        public override void VisitImplicitElementAccess(ImplicitElementAccessSyntax Node) { CheckAndMark(Node); base.VisitImplicitElementAccess(Node); }

        /// <summary>
        /// 访问构造函数初始化器节点。
        /// </summary>
        /// <param name="Node">构造函数初始化器节点</param>
        public override void VisitConstructorInitializer(ConstructorInitializerSyntax Node) { CheckAndMark(Node); base.VisitConstructorInitializer(Node); }

        /// <summary>
        /// 访问前缀一元表达式节点。
        /// </summary>
        /// <param name="Node">前缀一元表达式节点</param>
        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax Node) { CheckAndMark(Node); base.VisitPrefixUnaryExpression(Node); }

        /// <summary>
        /// 访问后缀一元表达式节点。
        /// </summary>
        /// <param name="Node">后缀一元表达式节点</param>
        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax Node) { CheckAndMark(Node); base.VisitPostfixUnaryExpression(Node); }

        /// <summary>
        /// 访问成员绑定表达式节点。
        /// </summary>
        /// <param name="Node">成员绑定表达式节点</param>
        public override void VisitMemberBindingExpression(MemberBindingExpressionSyntax Node) { CheckAndMark(Node); base.VisitMemberBindingExpression(Node); }

        /// <summary>
        /// 访问类型约束节点。
        /// </summary>
        /// <param name="Node">类型约束节点</param>
        public override void VisitTypeConstraint(TypeConstraintSyntax Node) { CheckAndMark(Node); base.VisitTypeConstraint(Node); }

        /// <summary>
        /// 访问类型参数约束子句节点。
        /// </summary>
        /// <param name="Node">类型参数约束子句节点</param>
        public override void VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax Node) { CheckAndMark(Node); base.VisitTypeParameterConstraintClause(Node); }

        /// <summary>
        /// 访问特性参数节点。
        /// </summary>
        /// <param name="Node">特性参数节点</param>
        public override void VisitAttributeArgument(AttributeArgumentSyntax Node) { CheckAndMark(Node); base.VisitAttributeArgument(Node); }

        /// <summary>
        /// 核心检查逻辑：检查节点关联的符号是否被标记并更新标记集合。
        /// </summary>
        /// <param name="Node">待检查的语法节点</param>
        protected abstract void CheckAndMark(SyntaxNode Node);
    }

    /// <summary>
    /// 方案一：节点追踪 (Node Tracking)
    /// 通过位置信息（FullSpan）将当前节点映射回原始语法树，确保语义查询安全性。
    /// </summary>
    public class NodeTrackingPropagator : PropagatorBase
    {
        private readonly SemanticModel Model;

        /// <summary>
        /// 初始化 <see cref="NodeTrackingPropagator"/> 类的新实例。
        /// </summary>
        /// <param name="Model">原始语义模型</param>
        /// <param name="NodesToMark">标记节点集合</param>
        public NodeTrackingPropagator(SemanticModel Model, HashSet<SyntaxNode> NodesToMark) : base(NodesToMark)
        {
            this.Model = Model;
        }

        /// <summary>
        /// 检查节点关联符号并标记引用。
        /// </summary>
        /// <param name="Node">当前语法节点</param>
        protected override void CheckAndMark(SyntaxNode Node)
        {
            if (NodesToMark.Contains(Node)) return;

            var NodeToQuery = GetQueryableNode(Node, Model);
            if (NodeToQuery.SyntaxTree != Model.SyntaxTree) return;

            var SymbolInfo = Model.GetSymbolInfo(NodeToQuery);
            var Symbol = SymbolInfo.Symbol ?? SymbolInfo.CandidateSymbols.FirstOrDefault();

            if (Symbol != null && IsSymbolMarked(Symbol))
            {
                NodesToMark.Add(Node);
                return;
            }

            // 特殊处理别名
            if (NodeToQuery is IdentifierNameSyntax Identifier)
            {
                var AliasInfo = Model.GetAliasInfo(Identifier);
                if (AliasInfo != null && IsSymbolMarked(AliasInfo))
                {
                    NodesToMark.Add(Node);
                }
            }
        }
    }

    /// <summary>
    /// 方案二：基于 Compilation 的动态模型获取 (Dynamic Model Acquisition)
    /// 利用 Compilation 动态获取与当前节点语法树匹配的语义模型。
    /// </summary>
    public class DynamicModelPropagator : PropagatorBase
    {
        private readonly Compilation Compilation;

        /// <summary>
        /// 初始化 <see cref="DynamicModelPropagator"/> 类的新实例。
        /// </summary>
        /// <param name="Compilation">编译对象</param>
        /// <param name="NodesToMark">标记节点集合</param>
        public DynamicModelPropagator(Compilation Compilation, HashSet<SyntaxNode> NodesToMark) : base(NodesToMark)
        {
            this.Compilation = Compilation;
        }

        /// <summary>
        /// 检查节点关联符号并标记引用。
        /// </summary>
        /// <param name="Node">当前语法节点</param>
        protected override void CheckAndMark(SyntaxNode Node)
        {
            if (NodesToMark.Contains(Node)) return;

            var Model = Compilation.GetSemanticModel(Node.SyntaxTree);
            // 即使获取了对应树的模型，如果该树是克隆/修改过的（不在编译中），
            // GetSemanticModel 可能会返回一个无法查询该节点的模型，或者抛出异常。
            // 但通常 GetSemanticModel(Node.SyntaxTree) 会返回一个能够查询该树的有效模型。

            var NodeToQuery = Node;
            try
            {
                var SymbolInfo = Model.GetSymbolInfo(NodeToQuery);
                var Symbol = SymbolInfo.Symbol ?? SymbolInfo.CandidateSymbols.FirstOrDefault();

                if (Symbol != null && IsSymbolMarked(Symbol))
                {
                    NodesToMark.Add(Node);
                    return;
                }

                if (NodeToQuery is IdentifierNameSyntax Identifier)
                {
                    var AliasInfo = Model.GetAliasInfo(Identifier);
                    if (AliasInfo != null && IsSymbolMarked(AliasInfo))
                    {
                        NodesToMark.Add(Node);
                    }
                }
            }
            catch (ArgumentException)
            {
                // 如果还是报错，说明 Node.SyntaxTree 虽然存在但不在编译生成的模型范围内
                // 这种情况下 DynamicModelPropagator 无法处理，直接跳过
            }
        }
    }

    /// <summary>
    /// 方案三：符号-引用映射预处理 (Symbol-Reference Mapping)
    /// 继承自 <see cref="PropagatorBase"/> 以复用基础逻辑。
    /// 预先建立符号与引用的映射表，传播阶段无需再进行重复的语义查询，大幅提升性能。
    /// </summary>
    public class PreprocessedSymbolPropagator : PropagatorBase
    {
        private readonly Dictionary<ISymbol, List<SyntaxNode>> UsageMap = new(SymbolEqualityComparer.Default);
        private readonly SemanticModel Model;

        /// <summary>
        /// 初始化 <see cref="PreprocessedSymbolPropagator"/> 类的新实例。
        /// </summary>
        /// <param name="Model">原始语义模型</param>
        /// <param name="NodesToMark">标记节点集合</param>
        /// <param name="Root">语法树根节点</param>
        public PreprocessedSymbolPropagator(SemanticModel Model, HashSet<SyntaxNode> NodesToMark, SyntaxNode Root) : base(NodesToMark)
        {
            this.Model = Model;
            // 初始阶段：通过遍历语法树构建符号引用映射表
            Visit(Root);
        }

        /// <summary>
        /// 核心逻辑：在构建映射表阶段，将节点与其关联符号建立映射关系。
        /// </summary>
        /// <param name="Node">当前语法节点</param>
        protected override void CheckAndMark(SyntaxNode Node)
        {
            var NodeToQuery = GetQueryableNode(Node, Model);
            if (NodeToQuery.SyntaxTree != Model.SyntaxTree) return;

            var SymbolInfo = Model.GetSymbolInfo(NodeToQuery);
            var Symbol = SymbolInfo.Symbol ?? SymbolInfo.CandidateSymbols.FirstOrDefault();

            if (Symbol != null)
            {
                AddMapping(Symbol, Node);
            }

            // 特殊处理别名信息
            if (NodeToQuery is IdentifierNameSyntax Identifier)
            {
                var Alias = Model.GetAliasInfo(Identifier);
                if (Alias != null)
                {
                    AddMapping(Alias, Node);
                }
            }
        }

        /// <summary>
        /// 添加符号到节点的映射。
        /// </summary>
        private void AddMapping(ISymbol Symbol, SyntaxNode Node)
        {
            if (!UsageMap.TryGetValue(Symbol, out var List))
            {
                List = new List<SyntaxNode>();
                UsageMap[Symbol] = List;
            }
            List.Add(Node);
        }

        /// <summary>
        /// 执行标记传播过程。
        /// 利用预构建的映射表，通过迭代直到标记集合不再变化。
        /// </summary>
        public void Propagate()
        {
            bool Changed;
            do
            {
                Changed = false;
                // 找出当前所有声明已被标记的符号
                var CurrentMarkedSymbols = UsageMap.Keys.Where(IsSymbolMarked).ToList();
                foreach (var Symbol in CurrentMarkedSymbols)
                {
                    // 将该符号的所有使用处节点都标记为移除
                    foreach (var Usage in UsageMap[Symbol])
                    {
                        if (NodesToMark.Add(Usage)) Changed = true;
                    }
                }
            } while (Changed);
        }
    }

    /// <summary>
    /// 方案四：调用方生命周期管理 (Lifecycle Managed Propagator)
    /// 包装类，执行传播前校验或同步一致性。
    /// </summary>
    public class LifecycleManagedPropagator : PropagatorBase
    {
        private SemanticModel Model;

        /// <summary>
        /// 初始化 <see cref="LifecycleManagedPropagator"/> 类的新实例。
        /// </summary>
        /// <param name="Model">初始语义模型</param>
        /// <param name="NodesToMark">标记节点集合</param>
        public LifecycleManagedPropagator(SemanticModel Model, HashSet<SyntaxNode> NodesToMark) : base(NodesToMark)
        {
            this.Model = Model;
        }

        /// <summary>
        /// 更新当前使用的语义模型。
        /// </summary>
        /// <param name="NewModel">新的语义模型</param>
        public void UpdateModel(SemanticModel NewModel)
        {
            this.Model = NewModel;
        }

        /// <summary>
        /// 检查节点关联符号并标记引用。
        /// </summary>
        /// <param name="Node">当前语法节点</param>
        protected override void CheckAndMark(SyntaxNode Node)
        {
            if (NodesToMark.Contains(Node)) return;

            // 1. 尝试使用当前模型进行位置映射查询（处理跨树但同源的情况）
            var NodeToQuery = GetQueryableNode(Node, Model);

            // 2. 如果映射失败且树确实不匹配，尝试切换模型
            if (NodeToQuery.SyntaxTree != Model.SyntaxTree)
            {
                try
                {
                    var NewModel = Model.Compilation.GetSemanticModel(Node.SyntaxTree);
                    if (NewModel != null)
                    {
                        Model = NewModel;
                        NodeToQuery = Node; // 使用切换后的新模型和原节点
                    }
                }
                catch { }
            }

            // 3. 最终确认节点在当前模型树中
            if (NodeToQuery.SyntaxTree != Model.SyntaxTree) return;

            var SymbolInfo = Model.GetSymbolInfo(NodeToQuery);
            var Symbol = SymbolInfo.Symbol ?? SymbolInfo.CandidateSymbols.FirstOrDefault();

            if (Symbol != null && IsSymbolMarked(Symbol))
            {
                NodesToMark.Add(Node);
                return;
            }

            if (NodeToQuery is IdentifierNameSyntax Identifier)
            {
                var AliasInfo = Model.GetAliasInfo(Identifier);
                if (AliasInfo != null && IsSymbolMarked(AliasInfo))
                {
                    NodesToMark.Add(Node);
                }
            }
        }
    }

    /// <summary>
    /// 默认的变量引用传播器。当前默认使用节点追踪策略。
    /// </summary>
    public class ReferencePropagator : NodeTrackingPropagator
    {
        /// <summary>
        /// 初始化 <see cref="ReferencePropagator"/> 类的新实例。
        /// </summary>
        /// <param name="Model">语义模型</param>
        /// <param name="NodesToMark">标记节点集合</param>
        public ReferencePropagator(SemanticModel Model, HashSet<SyntaxNode> NodesToMark) : base(Model, NodesToMark)
        {
        }
    }
}
