/**
 * 功能描述：提供基于 Roslyn 的 C# 语法树重写逻辑，用于简化、移除或替换语法节点。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 表达式简化重写器，执行实际的节点移除、简化或占位符替换。
    /// </summary>
    internal class ExpressionSimplifier : CSharpSyntaxRewriter
    {
        /// <summary>
        /// 决定节点是否应该被标记为移除的"谓词"函数。
        /// </summary>
        private readonly System.Func<SyntaxNode, bool> ShouldRemove;

        /// <summary>
        /// 初始被标记为移除的节点集合（在原始树中的节点）。
        /// 用于在重写阶段执行符号引用失效校验。
        /// </summary>
        private readonly System.Collections.Generic.HashSet<SyntaxNode>? NodesToMark;

        /// <summary>
        /// 关联的语义模型，用于在简化过程中进行复杂的类型分析和占位符生成。
        /// </summary>
        private readonly SemanticModel? Model;

        /// <summary>
        /// 初始化 ExpressionSimplifier 的新实例。
        /// </summary>
        /// <param name="ShouldRemove">判断节点是否需要移除的谓词委托</param>
        /// <param name="Model">可选的语义模型，用于增强分析能力</param>
        /// <param name="NodesToMark">可选的原始树中被标记的节点集合</param>
        public ExpressionSimplifier(System.Func<SyntaxNode, bool> ShouldRemove, SemanticModel? Model = null, System.Collections.Generic.HashSet<SyntaxNode>? NodesToMark = null)
        {
            this.ShouldRemove = ShouldRemove;
            this.Model = Model;
            this.NodesToMark = NodesToMark;
        }

        /// <summary>
        /// 在当前语义模型的语法树中查找与给定节点对应的原始节点。
        /// 处理语法树在重写过程中可能发生的结构变化。
        /// </summary>
        /// <param name="Node">待查找的节点</param>
        /// <returns>在原始语法树中的对应节点，若未找到或模型缺失则返回 null</returns>
        private SyntaxNode? GetOriginalNode(SyntaxNode? Node)
        {
            if (Node == null || this.Model == null) return null;
            // 如果节点本身就属于当前模型所在的树，直接返回
            if (Node.SyntaxTree == this.Model.SyntaxTree) return Node;

            try
            {
                // 通过 FullSpan 查找原始树中的对应节点
                var originalRoot = this.Model.SyntaxTree.GetRoot();
                if (Node.FullSpan.End <= originalRoot.FullSpan.End)
                {
                    var found = originalRoot.FindNode(Node.FullSpan, getInnermostNodeForTie: true);
                    // 额外校验：确保找到的节点确实属于模型的语法树
                    if (found != null && found.SyntaxTree == this.Model.SyntaxTree)
                    {
                        return found;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 获取语法节点的类型符号。
        /// </summary>
        /// <param name="Node">语法节点</param>
        /// <returns>类型符号 ITypeSymbol，若无法获取则返回 null</returns>
        private ITypeSymbol? GetNodeType(SyntaxNode? Node)
        {
            var Original = GetOriginalNode(Node);
            if (Original == null || this.Model == null) return null;
            var TypeInfo = this.Model.GetTypeInfo(Original);
            // 优先返回转换后的类型（ConvertedType），以处理隐式转换场景
            return TypeInfo.ConvertedType ?? TypeInfo.Type;
        }

        /// <summary>
        /// 检查节点是否属于"受保护"的声明类型。
        /// 诸如方法、类型、命名空间等结构性声明，通常不应被该重写器直接移除，
        /// 以避免破坏源文件的基本结构。
        /// 只有当 UpwardMarkCollector 明确标记了这些节点时，才允许移除。
        /// </summary>
        /// <param name="Node">语法节点</param>
        /// <returns>如果是受保护的声明则返回 true</returns>
        private bool IsProtectedDeclaration(SyntaxNode Node)
        {
            // 特殊情况：用户明确指定不应删除的函数
            if (Node is MethodDeclarationSyntax Method && Method.Identifier.ValueText == "HackForGamepadInputHell")
            {
                return true;
            }

            return Node is CompilationUnitSyntax
                || Node is ParameterListSyntax
                || Node is TypeParameterListSyntax
                || Node is ArgumentListSyntax
                || Node is AttributeArgumentListSyntax
                || Node is BracketedArgumentListSyntax
                || Node is VariableDesignationSyntax
                || Node is SingleVariableDesignationSyntax
                || Node is ParenthesizedVariableDesignationSyntax
                || Node is DiscardDesignationSyntax
            //    || Node is ReturnStatementSyntax
            //     || Node is BlockSyntax
                || (Node is BlockSyntax && IsFunctionBody(Node))
                || Node is ArgumentSyntax
                || Node is AttributeArgumentSyntax
                || Node is EqualsValueClauseSyntax
                || Node is AssignmentExpressionSyntax
                || Node is PatternSyntax
                || Node is SwitchExpressionArmSyntax;
        }

        private bool IsFunctionBody(SyntaxNode node)
        {
            var parent = node.Parent;
            return parent is MethodDeclarationSyntax
                || parent is ConstructorDeclarationSyntax
                || parent is DestructorDeclarationSyntax
                || parent is AccessorDeclarationSyntax
                || parent is LocalFunctionStatementSyntax
                || parent is AnonymousFunctionExpressionSyntax;
        }

        /// <summary>
        /// 判断给定符号的声明是否已被标记为移除。
        /// </summary>
        private bool IsSymbolMarked(ISymbol Symbol)
        {
            if (Symbol == null || NodesToMark == null) return false;
            foreach (var Reference in Symbol.DeclaringSyntaxReferences)
            {
                var Syntax = Reference.GetSyntax();
                var DeclaringSpan = Syntax.FullSpan;

                // 检查声明节点或其任何祖先是否在标记集合中
                if (NodesToMark.Any(M => M.FullSpan.Contains(DeclaringSpan)))
                {
                    return true;
                }
            }
            return false;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax Node)
        {
            if (this.ShouldRemove(Node)) return null;

            // 符号表引用失效验证：如果标识符引用的符号声明已被标记移除，则移除该引用
            if (this.Model != null)
            {
                var Original = GetOriginalNode(Node);
                if (Original != null)
                {
                    var SymbolInfo = this.Model.GetSymbolInfo(Original);
                    var Symbol = SymbolInfo.Symbol ?? SymbolInfo.CandidateSymbols.FirstOrDefault();
                    if (Symbol != null && IsSymbolMarked(Symbol))
                    {
                        return null;
                    }
                }
            }

            return base.VisitIdentifierName(Node);
        }

        public override SyntaxNode? VisitGenericName(GenericNameSyntax Node)
        {
            if (this.ShouldRemove(Node)) return null;

            // 同上：符号表引用失效验证
            if (this.Model != null)
            {
                var Original = GetOriginalNode(Node);
                if (Original != null)
                {
                    var SymbolInfo = this.Model.GetSymbolInfo(Original);
                    var Symbol = SymbolInfo.Symbol ?? SymbolInfo.CandidateSymbols.FirstOrDefault();
                    if (Symbol != null && IsSymbolMarked(Symbol))
                    {
                        return null;
                    }
                }
            }

            return base.VisitGenericName(Node);
        }

        /// <summary>
        /// 尝试根据节点的上下文创建一个语义占位符（如 default(T) 或 null）。
        /// 当一个节点被标记为移除，但其父节点要求必须存在一个表达式（如 return 语句或方法参数）时，
        /// 此方法会生成一个等效的占位符来填补空缺。
        /// </summary>
        /// <param name="Node">被标记为移除的节点</param>
        /// <returns>生成的占位符节点，若无需占位或无法生成则返回 null</returns>
        private SyntaxNode? TryCreatePlaceholder(SyntaxNode Node)
        {
            if (this.Model == null) return null;

            // 使用上下文感知判定是否需要占位符
            if (!IsValueRequiredContext(Node)) return null;

            try
            {
                // 1. 针对表达式节点，尝试通过类型信息生成通用占位符
                if (Node is ExpressionSyntax Expr)
                {
                    var Placeholder = TryGetPlaceholderExpression(Expr);
                    if (Placeholder != null) return Placeholder;
                }

                // 2. 针对 Return 语句的特殊处理：获取方法或属性的返回类型
                if (Node.Parent is ReturnStatementSyntax)
                {
                    var OriginalNode = GetOriginalNode(Node);
                    if (OriginalNode != null)
                    {
                        var Symbol = this.Model.GetEnclosingSymbol(OriginalNode.SpanStart);
                        ITypeSymbol? ReturnType = null;
                        if (Symbol is IMethodSymbol Method) ReturnType = Method.ReturnType;
                        else if (Symbol is IPropertySymbol Property) ReturnType = Property.Type;

                        // 如果是非 void 类型，则生成对应的占位符
                        if (ReturnType != null && ReturnType.SpecialType != SpecialType.System_Void)
                        {
                            return CreatePlaceholder(ReturnType);
                        }
                    }
                }
                // 3. 针对方法调用参数的特殊处理：根据形参类型生成占位符
                else if (Node.Parent is ArgumentSyntax Arg && Arg.Parent is ArgumentListSyntax ArgList && ArgList.Parent is InvocationExpressionSyntax Invocation)
                {
                    var OriginalInvocation = GetOriginalNode(Invocation) as InvocationExpressionSyntax;
                    if (OriginalInvocation != null)
                    {
                        var MethodSymbol = this.Model.GetSymbolInfo(OriginalInvocation).Symbol as IMethodSymbol;
                        int Index = ArgList.Arguments.IndexOf(Arg);
                        if (MethodSymbol != null && Index >= 0 && Index < MethodSymbol.Parameters.Length)
                        {
                            return CreatePlaceholder(MethodSymbol.Parameters[Index].Type);
                        }
                    }
                }
            }
            catch { /* 忽略异常 */ }

            return null;
        }

        /// <summary>
        /// 尝试获取表达式的语义占位符。
        /// </summary>
        private ExpressionSyntax? TryGetPlaceholderExpression(ExpressionSyntax Expression)
        {
            var Type = GetNodeType(Expression);
            if (Type == null)
            {
                // 尝试针对常量进行特殊处理
                if (Expression is LiteralExpressionSyntax Literal)
                {
                    if (Literal.IsKind(SyntaxKind.NumericLiteralExpression))
                        return SyntaxFactory.DefaultExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)));
                    if (Literal.IsKind(SyntaxKind.StringLiteralExpression))
                        return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                }
            }
            return Type != null ? CreatePlaceholder(Type) : null;
        }

        /// <summary>
        /// 检查给定节点所处的上下文是否必须要求一个值（表达式结果）。
        /// 例如：方法参数、Return 语句、赋值右侧等。
        /// </summary>
        /// <param name="Node">语法节点</param>
        /// <returns>如果必须要求值则返回 true</returns>
        private bool IsValueRequiredContext(SyntaxNode? Node)
        {
            if (Node == null) return false;
            var Parent = Node.Parent;
            if (Parent == null) return false;

            return Parent switch
            {
                // 显式要求值的上下文
                ArgumentSyntax => true,
                ReturnStatementSyntax => true,
                ArrowExpressionClauseSyntax => true,
                EqualsValueClauseSyntax => true,
                IfStatementSyntax ifStmt => ifStmt.Condition == Node,
                WhileStatementSyntax whileStmt => whileStmt.Condition == Node,
                DoStatementSyntax doStmt => doStmt.Condition == Node,
                ForStatementSyntax forStmt => forStmt.Condition == Node,
                SwitchStatementSyntax switchStmt => switchStmt.Expression == Node,
                ConditionalExpressionSyntax => true,

                // 在赋值表达式中，右侧必须有值
                AssignmentExpressionSyntax assign => assign.Right == Node,

                // 在二元表达式中，两侧通常都需要有值
                BinaryExpressionSyntax => true,

                // 默认情况：如果是表达式语句，则不需要值（可以整行删除）
                ExpressionStatementSyntax => false,

                _ => !(Parent is StatementSyntax) // 如果父节点不是语句，通常意味着它是更大表达式的一部分
            };
        }

        /// <summary>
        /// 访问块（Block）。
        /// 遍历块中的所有语句，移除被标记为删除的语句。
        /// </summary>
        /// <param name="Node">块节点</param>
        /// <returns>简化后的块节点</returns>
        public override SyntaxNode? VisitBlock(BlockSyntax Node)
        {
            var statements = VisitList(Node.Statements);
            return Node.WithStatements(statements);
        }

        /// <summary>
        /// 访问并重写语法节点。
        /// 该方法是重写器的核心入口，它首先检查当前节点是否符合"谓词"标记的移除条件。
        /// 如果符合条件且非受保护声明（由 IsProtectedDeclaration 判定），则尝试将其替换为占位符或直接移除（返回 null）。
        /// 否则，根据节点类型分发到对应的处理方法（如 HandleBinary, HandleInvocation 等）进行深度简化。
        /// </summary>
        /// <param name="Node">当前访问的节点</param>
        /// <returns>重写后的节点，如果节点被移除则返回 null</returns>
        public override SyntaxNode? Visit(SyntaxNode? Node)
        {
            if (Node == null) return null; // 如果节点为空，直接返回 null

            // 如果当前节点符合删除条件（通过 ShouldRemove 谓词判断）
            if (this.ShouldRemove(Node))
            {
                if (!IsProtectedDeclaration(Node))
                {
                    var placeholder = TryCreatePlaceholder(Node);
                    if (placeholder != null) return placeholder;
                    return null;
                }
                else if (NodesToMark != null && NodesToMark.Contains(Node))
                {
                    // 如果是受保护节点，但明确在 NodesToMark 中，则允许移除
                    return null;
                }
            }

            // 特殊处理：如果是受保护的 BlockSyntax 且被标记为移除，但不在 NodesToMark 中
            // 这种情况下，我们不应该分发到 VisitBlock，因为 VisitBlock 会基于 Node.Statements 重新创建 Block，
            // 而是应该根据分发逻辑继续访问其内容。
            if (Node is BlockSyntax && ShouldRemove(Node) && IsProtectedDeclaration(Node))
            {
                if (NodesToMark == null || !NodesToMark.Contains(Node))
                {
                    // 继续向下访问，而不是分发到可能导致节点保留的 VisitBlock
                    return base.Visit(Node);
                }
            }

            return Node switch // 根据节点类型进行分发处理，以实现更精细的简化逻辑
            {
                BlockSyntax Block => VisitBlock(Block), // 显式分发 BlockSyntax
                // 1. 基础表达式与结构
                BinaryExpressionSyntax Binary => HandleBinary(Binary), // 处理二元表达式
                ConditionalExpressionSyntax Ternary => HandleTernary(Ternary), // 处理三元表达式
                ParenthesizedExpressionSyntax Paren => HandleParenthesized(Paren), // 处理括号表达式

                // 2. 一元运算与异步
                PrefixUnaryExpressionSyntax Prefix => HandlePrefixUnary(Prefix), // 处理前缀一元表达式
                PostfixUnaryExpressionSyntax Postfix => HandlePostfixUnary(Postfix), // 处理后缀一元表达式
                AwaitExpressionSyntax AwaitExpr => HandleAwait(AwaitExpr), // 处理 Await 表达式

                // 3. 访问与调用
                MemberAccessExpressionSyntax Member => HandleMemberAccess(Member), // 处理成员访问
                ElementAccessExpressionSyntax Element => HandleElementAccess(Element), // 处理元素访问
                InvocationExpressionSyntax Invocation => HandleInvocation(Invocation), // 处理方法调用
                ConditionalAccessExpressionSyntax CondAccess => HandleConditionalAccess(CondAccess), // 处理条件访问

                // 4. 创建与初始化
                BaseObjectCreationExpressionSyntax BaseObjCreate => HandleBaseObjectCreation(BaseObjCreate), // 处理对象创建
                AnonymousObjectCreationExpressionSyntax AnonObj => HandleAnonymousObjectCreation(AnonObj), // 处理匿名对象创建
                ArrayCreationExpressionSyntax ArrayCreate => HandleArrayCreation(ArrayCreate), // 处理数组创建
                ImplicitArrayCreationExpressionSyntax ImpArray => HandleImplicitArrayCreation(ImpArray), // 处理隐式数组创建
                StackAllocArrayCreationExpressionSyntax StackAlloc => HandleStackAllocArrayCreation(StackAlloc), // 处理栈分配数组创建
                ImplicitStackAllocArrayCreationExpressionSyntax ImpStackAlloc => HandleImplicitStackAllocArrayCreation(ImpStackAlloc), // 处理隐式栈分配数组创建
                InitializerExpressionSyntax Init => HandleInitializer(Init), // 处理初始化器
                CollectionExpressionSyntax Collection => HandleCollection(Collection), // 处理集合表达式

                // 5. 类型与模式
                CastExpressionSyntax Cast => HandleCast(Cast), // 处理强制转换
                TypeOfExpressionSyntax TypeOf => HandleTypeOf(TypeOf), // 处理 typeof 表达式
                IsPatternExpressionSyntax IsPattern => HandleIsPattern(IsPattern), // 处理 is 模式匹配
                RefTypeExpressionSyntax RefType => HandleRefType(RefType), // 处理 ref 类型
                SizeOfExpressionSyntax SizeOf => HandleSizeOf(SizeOf), // 处理 sizeof 表达式
                ArrayTypeSyntax ArrayType => HandleArrayType(ArrayType), // 处理数组类型
                TypeSyntax Type => HandleType(Type), // 处理一般类型

                // 6. 现代 C# 特性与特殊表达式
                SwitchExpressionSyntax @Switch => HandleSwitch(@Switch), // 处理 switch 表达式
                WithExpressionSyntax @With => HandleWith(@With), // 处理 with 表达式
                InterpolatedStringExpressionSyntax Interp => HandleInterpolatedString(Interp), // 处理内插字符串
                TupleExpressionSyntax TupleExpr => VisitTupleExpression(TupleExpr), // 处理元组表达式
                RangeExpressionSyntax Range => HandleRange(Range), // 处理范围表达式
                CheckedExpressionSyntax CheckedExpr => HandleChecked(CheckedExpr), // 处理 checked 表达式
                DefaultExpressionSyntax DefaultExpr => HandleDefault(DefaultExpr), // 处理 default 表达式
                QueryExpressionSyntax Query => HandleQuery(Query), // 处理 LINQ 查询表达式

                // 7. 声明、赋值与引用
                AssignmentExpressionSyntax Assign => HandleAssignment(Assign), // 处理赋值表达式
                DeclarationExpressionSyntax Decl => HandleDeclaration(Decl), // 处理声明表达式
                RefExpressionSyntax @Ref => HandleRef(@Ref), // 处理 ref 表达式
                RefValueExpressionSyntax RefVal => HandleRefValue(RefVal), // 处理 refvalue 表达式
                MakeRefExpressionSyntax MakeRef => HandleMakeRef(MakeRef), // 处理 makeref 表达式

                // 8. 绑定与访问
                ElementBindingExpressionSyntax ElemBind => HandleElementBinding(ElemBind), // 处理元素绑定
                MemberBindingExpressionSyntax MembBind => HandleMemberBinding(MembBind), // 处理成员绑定
                ImplicitElementAccessSyntax ImpElemAccess => HandleImplicitElementAccess(ImpElemAccess), // 处理隐式元素访问
                InstanceExpressionSyntax Instance => HandleInstance(Instance), // 处理实例表达式（this, base）
                OmittedArraySizeExpressionSyntax OmittedSize => HandleOmittedArraySize(OmittedSize), // 处理省略的数组大小

                // 9. 基础
                LiteralExpressionSyntax Literal => HandleLiteral(Literal), // 处理字面量
                ThrowExpressionSyntax @Throw => HandleThrow(@Throw), // 处理 throw 表达式
                AnonymousFunctionExpressionSyntax AnonFunc => HandleAnonymousFunction(AnonFunc), // 处理匿名函数（lambda, delegate）

                ArgumentSyntax Arg => VisitArgument(Arg),
                ArgumentListSyntax ArgList => VisitArgumentList(ArgList),
                AttributeSyntax Attr => VisitAttribute(Attr),
                AttributeArgumentSyntax AttrArg => VisitAttributeArgument(AttrArg),
                AttributeArgumentListSyntax AttrArgList => VisitAttributeArgumentList(AttrArgList),

                UsingStatementSyntax @Using => VisitUsingStatement(@Using),
                LockStatementSyntax @Lock => VisitLockStatement(@Lock),
                FixedStatementSyntax @Fixed => VisitFixedStatement(@Fixed),
                CheckedStatementSyntax @Checked => VisitCheckedStatement(@Checked),
                TryStatementSyntax @Try => VisitTryStatement(@Try),
                FinallyClauseSyntax @Finally => VisitFinallyClause(@Finally),
                TypeParameterConstraintClauseSyntax @Constraint => VisitTypeParameterConstraintClause(@Constraint),

                _ => base.Visit(Node) // 其他情况调用基类访问方法
            };
        }

        /// <summary>
        /// 访问并处理普通语法节点列表。
        /// 遍历列表中的每个元素，移除标记为删除的节点，并根据节点变化情况返回更新后的列表。
        /// </summary>
        /// <typeparam name="T">节点类型</typeparam>
        /// <param name="list">原始语法节点列表</param>
        /// <returns>处理后的语法节点列表</returns>
        public override SyntaxList<T> VisitList<T>(SyntaxList<T> list)
        {
            var newList = new System.Collections.Generic.List<T>();
            bool changed = false;
            foreach (var item in list)
            {
                var visited = (T?)Visit(item);
                if (visited != null)
                {
                    newList.Add(visited);
                }
                if (visited != item)
                {
                    changed = true;
                }
            }

            if (newList.Count != list.Count)
            {
                changed = true;
            }

            return changed ? SyntaxFactory.List(newList) : list;
        }

        /// <summary>
        /// 访问并处理带分隔符的语法节点列表（如参数列表、变量声明列表）。
        /// 遍历列表中的每个元素，移除标记为删除的节点，并根据节点变化情况返回更新后的分隔列表。
        /// </summary>
        /// <typeparam name="T">节点类型</typeparam>
        /// <param name="list">原始带分隔符的语法节点列表</param>
        /// <returns>处理后的带分隔符的语法节点列表</returns>
        public override SeparatedSyntaxList<T> VisitList<T>(SeparatedSyntaxList<T> list)
        {
            var newList = new System.Collections.Generic.List<T>();
            bool changed = false;
            foreach (var item in list)
            {
                var visited = (T?)Visit(item);
                if (visited != null)
                {
                    newList.Add(visited);
                }
                if (visited != item)
                {
                    changed = true;
                }
            }

            if (newList.Count != list.Count)
            {
                changed = true;
            }

            return changed ? SyntaxFactory.SeparatedList(newList) : list;
        }

        /// <summary>
        /// 访问命名空间声明。
        /// 若命名空间名称被移除，则整个声明移除；否则更新其内部的成员、外部声明和引用。
        /// </summary>
        /// <param name="Node">命名空间声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax Node)
        {
            var name = (NameSyntax?)Visit(Node.Name);
            if (name == null) return null;
            var members = VisitList(Node.Members);
            var externs = VisitList(Node.Externs);
            var usings = VisitList(Node.Usings);
            return Node.Update(Node.NamespaceKeyword, name, Node.OpenBraceToken, externs, usings, members, Node.CloseBraceToken, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问 Using 指令。
        /// 若引用的名称被移除，则整个 Using 指令移除。
        /// </summary>
        /// <param name="Node">Using 指令节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax Node)
        {
            var name = (NameSyntax?)Visit(Node.Name);
            if (name == null) return null;
            return Node.Update(Node.GlobalKeyword, Node.UsingKeyword, Node.StaticKeyword, Node.Alias, name, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问字段声明。
        /// 若变量声明部分被移除，则整个字段声明移除。
        /// </summary>
        /// <param name="Node">字段声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax Node)
        {
            var declaration = (VariableDeclarationSyntax?)Visit(Node.Declaration);
            if (declaration == null) return null;
            var attributeLists = VisitList(Node.AttributeLists);
            return Node.Update(attributeLists, Node.Modifiers, declaration, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问类声明。
        /// 更新类内部的成员、基类列表、约束子句、特性列表及主构造函数参数。
        /// </summary>
        /// <param name="Node">类声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax Node)
        {
            var members = VisitList(Node.Members);
            var baseList = (BaseListSyntax?)Visit(Node.BaseList);
            var constraintClauses = VisitList(Node.ConstraintClauses);
            var attributeLists = VisitList(Node.AttributeLists);
            var parameterList = (ParameterListSyntax?)Visit(Node.ParameterList);

            return Node.Update(attributeLists, Node.Modifiers, Node.Keyword, Node.Identifier, Node.TypeParameterList, parameterList, baseList, constraintClauses, Node.OpenBraceToken, members, Node.CloseBraceToken, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问基类列表。
        /// 若所有基类或接口都被移除，则整个基类列表移除。
        /// </summary>
        /// <param name="Node">基类列表节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitBaseList(BaseListSyntax Node)
        {
            var types = VisitList(Node.Types);
            if (types.Count == 0) return null;
            return Node.Update(Node.ColonToken, types);
        }

        /// <summary>
        /// 访问简单基类型。
        /// 若类型部分被移除，则整个节点移除。
        /// </summary>
        /// <param name="Node">简单基类型节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitSimpleBaseType(SimpleBaseTypeSyntax Node)
        {
            var type = (TypeSyntax?)Visit(Node.Type);
            if (type == null) return null;
            return Node.Update(type);
        }

        /// <summary>
        /// 访问特性列表（方括号括起来的一组特性）。
        /// 若列表中所有特性都被移除，则整个特性列表移除。
        /// </summary>
        /// <param name="Node">特性列表节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitAttributeList(AttributeListSyntax Node)
        {
            var attributes = VisitList(Node.Attributes);
            if (attributes.Count == 0) return null;
            return Node.Update(Node.OpenBracketToken, Node.Target, attributes, Node.CloseBracketToken);
        }

        /// <summary>
        /// 访问方法声明。
        /// 若返回类型或参数列表被移除，则整个方法声明移除；否则更新方法体、约束及特性。
        /// </summary>
        /// <param name="Node">方法声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax Node)
        {
            var returnType = (TypeSyntax?)Visit(Node.ReturnType);
            var parameterList = (ParameterListSyntax?)Visit(Node.ParameterList);
            if (returnType == null || parameterList == null) return null;

            var constraintClauses = VisitList(Node.ConstraintClauses);
            var attributeLists = VisitList(Node.AttributeLists);

            return Node.Update(
                attributeLists,
                Node.Modifiers,
                returnType,
                Node.ExplicitInterfaceSpecifier,
                Node.Identifier,
                Node.TypeParameterList,
                parameterList,
                constraintClauses,
                (BlockSyntax?)Visit(Node.Body),
                (ArrowExpressionClauseSyntax?)Visit(Node.ExpressionBody),
                Node.SemicolonToken);
        }

        /// <summary>
        /// 访问结构体声明。
        /// 更新结构体内部成员、基类列表、约束及特性。
        /// </summary>
        /// <param name="Node">结构体声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax Node)
        {
            var members = VisitList(Node.Members);
            var baseList = (BaseListSyntax?)Visit(Node.BaseList);
            var constraintClauses = VisitList(Node.ConstraintClauses);
            var attributeLists = VisitList(Node.AttributeLists);
            var parameterList = (ParameterListSyntax?)Visit(Node.ParameterList);

            return Node.Update(attributeLists, Node.Modifiers, Node.Keyword, Node.Identifier, Node.TypeParameterList, parameterList, baseList, constraintClauses, Node.OpenBraceToken, members, Node.CloseBraceToken, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问接口声明。
        /// 更新接口成员、继承列表、约束及特性。
        /// </summary>
        /// <param name="Node">接口声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax Node)
        {
            var members = VisitList(Node.Members);
            var baseList = (BaseListSyntax?)Visit(Node.BaseList);
            var constraintClauses = VisitList(Node.ConstraintClauses);
            var attributeLists = VisitList(Node.AttributeLists);
            return Node.Update(attributeLists, Node.Modifiers, Node.Keyword, Node.Identifier, Node.TypeParameterList, baseList, constraintClauses, Node.OpenBraceToken, members, Node.CloseBraceToken, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问记录声明（Record）。
        /// 更新记录成员、基类列表、参数列表及特性。
        /// </summary>
        /// <param name="Node">记录声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax Node)
        {
            var members = VisitList(Node.Members);
            var baseList = (BaseListSyntax?)Visit(Node.BaseList);
            var constraintClauses = VisitList(Node.ConstraintClauses);
            var attributeLists = VisitList(Node.AttributeLists);
            var parameterList = (ParameterListSyntax?)Visit(Node.ParameterList);

            return Node.Update(attributeLists, Node.Modifiers, Node.Keyword, Node.ClassOrStructKeyword, Node.Identifier, Node.TypeParameterList, parameterList, baseList, constraintClauses, Node.OpenBraceToken, members, Node.CloseBraceToken, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问委托声明。
        /// 若返回类型或参数列表被移除，则整个委托声明移除。
        /// </summary>
        /// <param name="Node">委托声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax Node)
        {
            var returnType = (TypeSyntax?)Visit(Node.ReturnType);
            var parameterList = (ParameterListSyntax?)Visit(Node.ParameterList);
            if (returnType == null || parameterList == null) return null;
            var constraintClauses = VisitList(Node.ConstraintClauses);
            var attributeLists = VisitList(Node.AttributeLists);
            return Node.Update(attributeLists, Node.Modifiers, Node.DelegateKeyword, returnType, Node.Identifier, Node.TypeParameterList, parameterList, constraintClauses, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问属性声明。
        /// 若属性类型、访问器列表或表达式主体被移除，则整个属性声明移除。
        /// </summary>
        /// <param name="Node">属性声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax Node)
        {
            var type = (TypeSyntax?)Visit(Node.Type);
            if (type == null) return null;

            var accessorList = (AccessorListSyntax?)Visit(Node.AccessorList);
            var expressionBody = (ArrowExpressionClauseSyntax?)Visit(Node.ExpressionBody);

            // 如果原先有主体/表达式主体，但简化后都没了，则移除属性
            if ((Node.AccessorList != null && accessorList == null) || (Node.ExpressionBody != null && expressionBody == null))
            {
                return null;
            }

            return Node.Update(
                Node.AttributeLists,
                Node.Modifiers,
                type,
                Node.ExplicitInterfaceSpecifier,
                Node.Identifier,
                accessorList,
                expressionBody,
                Node.Initializer,
                Node.SemicolonToken);
        }

        /// <summary>
        /// 访问索引器声明。
        /// 若索引器类型或参数列表被移除，则整个索引器声明移除。
        /// </summary>
        /// <param name="Node">索引器声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax Node)
        {
            var type = (TypeSyntax?)Visit(Node.Type);
            var parameterList = (BracketedParameterListSyntax?)Visit(Node.ParameterList);
            if (type == null || parameterList == null) return null;

            return Node.Update(
                Node.AttributeLists,
                Node.Modifiers,
                type,
                Node.ExplicitInterfaceSpecifier,
                Node.ThisKeyword,
                parameterList,
                (AccessorListSyntax?)Visit(Node.AccessorList),
                (ArrowExpressionClauseSyntax?)Visit(Node.ExpressionBody),
                Node.SemicolonToken);
        }

        /// <summary>
        /// 访问构造函数声明。
        /// 若参数列表被移除，则整个构造函数声明移除。
        /// </summary>
        /// <param name="Node">构造函数声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax Node)
        {
            var parameterList = (ParameterListSyntax?)Visit(Node.ParameterList);
            if (parameterList == null) return null;

            var initializer = (ConstructorInitializerSyntax?)Visit(Node.Initializer);

            return Node.Update(
                Node.AttributeLists,
                Node.Modifiers,
                Node.Identifier,
                parameterList,
                initializer,
                (BlockSyntax?)Visit(Node.Body),
                (ArrowExpressionClauseSyntax?)Visit(Node.ExpressionBody),
                Node.SemicolonToken);
        }

        /// <summary>
        /// 访问构造函数初始化器（base(...) 或 this(...)）。
        /// 若参数列表被移除或为空，则初始化器移除。
        /// </summary>
        /// <param name="Node">构造函数初始化器节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitConstructorInitializer(ConstructorInitializerSyntax Node)
        {
            var argumentList = (ArgumentListSyntax?)Visit(Node.ArgumentList);
            if (argumentList == null) return null;
            if (argumentList.Arguments.Count == 0) return null;
            return Node.WithArgumentList(argumentList);
        }

        /// <summary>
        /// 访问方括号括起来的参数列表（通常用于索引器或特性）。
        /// </summary>
        /// <param name="Node">方括号参数列表节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitBracketedArgumentList(BracketedArgumentListSyntax Node)
        {
            var arguments = VisitList(Node.Arguments);
            return Node.Update(Node.OpenBracketToken, arguments, Node.CloseBracketToken);
        }

        /// <summary>
        /// 访问参数列表（圆括号）。
        /// 若对于类、记录或结构体声明，其主构造函数参数列表为空，则移除该列表。
        /// </summary>
        /// <param name="Node">参数列表节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitParameterList(ParameterListSyntax Node)
        {
            var parameters = VisitList(Node.Parameters);
            if (parameters.Count == 0 && (Node.Parent is ClassDeclarationSyntax || Node.Parent is RecordDeclarationSyntax || Node.Parent is StructDeclarationSyntax))
            {
                return null;
            }
            return Node.Update(Node.OpenParenToken, parameters, Node.CloseParenToken);
        }

        /// <summary>
        /// 访问方括号括起来的参数列表（用于索引器定义）。
        /// </summary>
        /// <param name="Node">方括号参数定义列表节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitBracketedParameterList(BracketedParameterListSyntax Node)
        {
            var parameters = VisitList(Node.Parameters);
            return Node.Update(Node.OpenBracketToken, parameters, Node.CloseBracketToken);
        }

        /// <summary>
        /// 访问箭头表达式子句（=> 表达式）。
        /// 若表达式部分被移除，则子句移除。
        /// </summary>
        /// <param name="Node">箭头表达式子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitArrowExpressionClause(ArrowExpressionClauseSyntax Node)
        {
            var expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (expression == null) return null;
            return Node.Update(Node.ArrowToken, expression);
        }

        /// <summary>
        /// 访问 try 语句。
        /// 若 try 块被移除，或者 catch 和 finally 均被移除，则整个 try 语句移除。
        /// </summary>
        /// <param name="Node">try 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitTryStatement(TryStatementSyntax Node)
        {
            var block = (BlockSyntax?)Visit(Node.Block);
            if (block == null) return null;

            var catches = VisitList(Node.Catches);
            var @finally = (FinallyClauseSyntax?)Visit(Node.Finally);

            if (catches.Count == 0 && @finally == null)
            {
                // 如果没有 catch 和 finally，只保留 try 块的内容（可能需要转换为语句列表，或者干脆移除整个 try）
                // 这里选择移除，因为通常 try 块是为了异常处理
                return null;
            }

            return Node.Update(Node.TryKeyword, block, catches, @finally!);
        }

        /// <summary>
        /// 访问 finally 子句。
        /// 若 finally 块被移除，则整个子句移除。
        /// </summary>
        /// <param name="Node">finally 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitFinallyClause(FinallyClauseSyntax Node)
        {
            var block = (BlockSyntax?)Visit(Node.Block);
            if (block == null) return null;

            return Node.Update(Node.FinallyKeyword, block);
        }

        /// <summary>
        /// 访问 using 语句。
        /// 若主体被移除，或资源声明/表达式被移除，则整个 using 语句移除。
        /// </summary>
        /// <param name="Node">using 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax Node)
        {
            var declaration = (VariableDeclarationSyntax?)Visit(Node.Declaration);
            var expression = (ExpressionSyntax?)Visit(Node.Expression);
            var statement = (StatementSyntax?)Visit(Node.Statement);

            // 如果主体被移除，或者（有声明且声明被移除），或者（有表达式且表达式被移除），则移除整个 using
            if (statement == null || (Node.Declaration != null && declaration == null) || (Node.Expression != null && expression == null))
            {
                return null;
            }

            return Node.Update(Node.AttributeLists, Node.AwaitKeyword, Node.UsingKeyword, Node.OpenParenToken, declaration, expression, Node.CloseParenToken, statement);
        }

        /// <summary>
        /// 访问 lock 语句。
        /// 若锁定表达式或主体语句被移除，则整个 lock 语句移除。
        /// </summary>
        /// <param name="Node">lock 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitLockStatement(LockStatementSyntax Node)
        {
            var expression = (ExpressionSyntax?)Visit(Node.Expression);
            var statement = (StatementSyntax?)Visit(Node.Statement);

            if (expression == null || statement == null) return null;

            return Node.Update(Node.LockKeyword, Node.OpenParenToken, expression, Node.CloseParenToken, statement);
        }

        /// <summary>
        /// 访问 fixed 语句。
        /// 若指针变量声明或主体语句被移除，则整个 fixed 语句移除。
        /// </summary>
        /// <param name="Node">fixed 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax Node)
        {
            var declaration = (VariableDeclarationSyntax?)Visit(Node.Declaration);
            var statement = (StatementSyntax?)Visit(Node.Statement);

            if (declaration == null || statement == null) return null;

            return Node.Update(Node.FixedKeyword, Node.OpenParenToken, declaration, Node.CloseParenToken, statement);
        }

        /// <summary>
        /// 访问 unsafe 语句块。
        /// 若块内部内容被移除，则整个 unsafe 语句移除。
        /// </summary>
        /// <param name="Node">unsafe 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitUnsafeStatement(UnsafeStatementSyntax Node)
        {
            var block = (BlockSyntax?)Visit(Node.Block);
            if (block == null) return null;
            return Node.Update(Node.UnsafeKeyword, block);
        }

        /// <summary>
        /// 访问 checked/unchecked 语句块。
        /// 若块内部内容被移除，则整个语句移除。
        /// </summary>
        /// <param name="Node">checked/unchecked 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitCheckedStatement(CheckedStatementSyntax Node)
        {
            var block = (BlockSyntax?)Visit(Node.Block);
            if (block == null) return null;
            return Node.Update(Node.Keyword, block);
        }

        /// <summary>
        /// 访问本地函数声明。
        /// 若本地函数的方法体和表达式主体都被移除，则整个本地函数声明移除。
        /// </summary>
        /// <param name="Node">本地函数声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax Node)
        {
            var body = (BlockSyntax?)Visit(Node.Body);
            var expressionBody = (ArrowExpressionClauseSyntax?)Visit(Node.ExpressionBody);
            if (body == null && expressionBody == null) return null;

            return Node.Update(
                Node.AttributeLists,
                Node.Modifiers,
                Node.ReturnType,
                Node.Identifier,
                Node.TypeParameterList,
                Node.ParameterList,
                Node.ConstraintClauses,
                body,
                expressionBody,
                Node.SemicolonToken);
        }

        /// <summary>
        /// 访问类型参数约束子句（where T : ...）。
        /// 若所有约束都被移除，则整个子句移除。
        /// </summary>
        /// <param name="Node">类型参数约束子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax Node)
        {
            var constraints = VisitList(Node.Constraints);
            if (constraints.Count == 0) return null;

            return Node.Update(Node.WhereKeyword, Node.Name, Node.ColonToken, constraints);
        }

        /// <summary>
        /// 访问声明模式（如 is int x）。
        /// 若类型部分被移除，则模式失效；若变量标识被移除，则用弃元（_）替换。
        /// </summary>
        /// <param name="Node">声明模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitDeclarationPattern(DeclarationPatternSyntax Node)
        {
            var type = (TypeSyntax?)Visit(Node.Type);
            var designation = (VariableDesignationSyntax?)Visit(Node.Designation);

            if (type == null) return null;
            if (designation == null)
            {
                designation = SyntaxFactory.DiscardDesignation();
            }

            return Node.Update(type, designation);
        }

        /// <summary>
        /// 访问递归模式（如 is { X: 1 }）。
        /// 若类型和所有子模式都被移除，则模式失效。
        /// </summary>
        /// <param name="Node">递归模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitRecursivePattern(RecursivePatternSyntax Node)
        {
            var type = (TypeSyntax?)Visit(Node.Type);
            var positionalPatternClause = (PositionalPatternClauseSyntax?)Visit(Node.PositionalPatternClause);
            var propertyPatternClause = (PropertyPatternClauseSyntax?)Visit(Node.PropertyPatternClause);
            var designation = (VariableDesignationSyntax?)Visit(Node.Designation);

            // 如果没有类型且没有子模式，则移除
            if (type == null && positionalPatternClause == null && propertyPatternClause == null) return null;

            return Node.Update(type, positionalPatternClause, propertyPatternClause, designation);
        }

        /// <summary>
        /// 访问 var 模式（如 is var x）。
        /// 若变量标识被移除，则用弃元替换。
        /// </summary>
        /// <param name="Node">var 模式节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitVarPattern(VarPatternSyntax Node)
        {
            var designation = (VariableDesignationSyntax?)Visit(Node.Designation);
            if (designation == null)
            {
                designation = SyntaxFactory.DiscardDesignation();
            }
            return Node.Update(Node.VarKeyword, designation);
        }

        /// <summary>
        /// 访问常量模式（如 is 100）。
        /// 若常量表达式被移除，则模式失效。
        /// </summary>
        /// <param name="Node">常量模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitConstantPattern(ConstantPatternSyntax Node)
        {
            var expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (expression == null) return null;
            return Node.Update(expression);
        }

        /// <summary>
        /// 访问二元模式（如 P1 and P2）。
        /// 若一边被移除，则返回另一边；若两边都移除，则返回 null。
        /// </summary>
        /// <param name="Node">二元模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitBinaryPattern(BinaryPatternSyntax Node)
        {
            var left = (PatternSyntax?)Visit(Node.Left);
            var right = (PatternSyntax?)Visit(Node.Right);

            if (left == null) return right;
            if (right == null) return left;

            return Node.Update(left, Node.OperatorToken, right);
        }

        /// <summary>
        /// 访问一元模式（如 not P）。
        /// 若子模式被移除，则整个模式失效。
        /// </summary>
        /// <param name="Node">一元模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitUnaryPattern(UnaryPatternSyntax Node)
        {
            var pattern = (PatternSyntax?)Visit(Node.Pattern);
            if (pattern == null) return null;
            return Node.Update(Node.OperatorToken, pattern);
        }

        /// <summary>
        /// 访问关系模式（如 is > 0）。
        /// 若比较表达式被移除，则模式失效。
        /// </summary>
        /// <param name="Node">关系模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitRelationalPattern(RelationalPatternSyntax Node)
        {
            var expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (expression == null) return null;
            return Node.Update(Node.OperatorToken, expression);
        }

        /// <summary>
        /// 访问类型模式（如 is int）。
        /// 若类型被移除，则模式失效。
        /// </summary>
        /// <param name="Node">类型模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitTypePattern(TypePatternSyntax Node)
        {
            var type = (TypeSyntax?)Visit(Node.Type);
            if (type == null) return null;
            return Node.Update(type);
        }

        /// <summary>
        /// 访问带括号的模式。
        /// 若内部模式被移除，则整个节点移除。
        /// </summary>
        /// <param name="Node">括号模式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitParenthesizedPattern(ParenthesizedPatternSyntax Node)
        {
            var pattern = (PatternSyntax?)Visit(Node.Pattern);
            if (pattern == null) return null;
            return Node.Update(Node.OpenParenToken, pattern, Node.CloseParenToken);
        }

        /// <summary>
        /// 访问带括号的变量标识列表（用于元组解构模式）。
        /// 若所有变量都被移除，则节点移除。
        /// </summary>
        /// <param name="Node">括号变量标识列表节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitParenthesizedVariableDesignation(ParenthesizedVariableDesignationSyntax Node)
        {
            var variables = VisitList(Node.Variables);
            if (variables.Count == 0) return null;
            return Node.Update(Node.OpenParenToken, variables, Node.CloseParenToken);
        }

        /// <summary>
        /// 访问单个变量标识。
        /// 若该标识标记为移除，则用弃元（_）替换，以保持模式结构。
        /// </summary>
        /// <param name="Node">单变量标识节点</param>
        /// <returns>简化后的节点（可能是弃元）</returns>
        public override SyntaxNode? VisitSingleVariableDesignation(SingleVariableDesignationSyntax Node)
        {
            if (this.ShouldRemove(Node))
            {
                return SyntaxFactory.DiscardDesignation();
            }
            return base.VisitSingleVariableDesignation(Node);
        }

        /// <summary>
        /// 访问弃元标识（_）。
        /// </summary>
        /// <param name="Node">弃元标识节点</param>
        /// <returns>节点本身</returns>
        public override SyntaxNode? VisitDiscardDesignation(DiscardDesignationSyntax Node)
        {
            return base.VisitDiscardDesignation(Node);
        }

        /// <summary>
        /// 处理二元表达式的简化。
        /// </summary>
        /// <param name="Node">待处理的二元表达式节点</param>
        /// <returns>简化后的表达式节点</returns>
        private SyntaxNode? HandleBinary(BinaryExpressionSyntax Node)
        {
            var Left = Visit(Node.Left); // 访问左侧表达式
            var Right = Visit(Node.Right); // 访问右侧表达式

            if (Left == null && Right == null)
            {
                // 上下文感知模式：如果当前二元表达式所在位置必须要求一个值，则返回占位符；否则移除。
                if (IsValueRequiredContext(Node))
                {
                    return TryGetPlaceholderExpression(Node);
                }
                return null;
            }

            // 对于逻辑运算符 (&&, ||)，如果一边为空，则返回另一边
            if (Node.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
            {
                if (Left == null) return Right?.WithLeadingTrivia(Node.GetLeadingTrivia());
                if (Right == null) return Left.WithTrailingTrivia(Node.GetTrailingTrivia());
            }

            // 对于算术运算符 (+, -, *, /)，如果一边为空，且另一边是有效表达式，则直接返回另一边
            // 这样可以避免生成 a + default(int) 这种冗余代码，直接简化为 a
            if (Node.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
                SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or
                SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or
                SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression or
                SyntaxKind.ExclusiveOrExpression or SyntaxKind.ModuloExpression)
            {
                if (Left == null && Right is ExpressionSyntax) return Right.WithLeadingTrivia(Node.GetLeadingTrivia());
                if (Right == null && Left is ExpressionSyntax) return Left.WithTrailingTrivia(Node.GetTrailingTrivia());
            }

            // 对于比较/关系运算符，如果一边被移除，整个表达式失效
            if (Node.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or
                SyntaxKind.GreaterThanExpression or SyntaxKind.LessThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.IsExpression or SyntaxKind.AsExpression)
            {
                if (Left == null || Right == null)
                {
                    // 上下文感知模式：如果必须要求值（如在 return 中），则返回占位符；否则移除（如在 if 中触发 if 的移除）
                    if (IsValueRequiredContext(Node))
                    {
                        return TryGetPlaceholderExpression(Node);
                    }
                    return null;
                }
            }

            // 如果存在语义模型，尝试使用占位符以保留表达式结构
            if (Left == null && this.Model != null)
            {
                var Type = GetNodeType(Node.Left);
                if (Type != null) Left = CreatePlaceholder(Type).WithLeadingTrivia(Node.GetLeadingTrivia());
            }

            if (Right == null && this.Model != null)
            {
                var Type = GetNodeType(Node.Right);
                if (Type != null) Right = CreatePlaceholder(Type).WithTrailingTrivia(Node.GetTrailingTrivia());
            }

            if (Left == null) return Right?.WithLeadingTrivia(Node.GetLeadingTrivia()); // 如果左侧为空，返回右侧并保留前置琐碎内容
            if (Right == null) return Left.WithTrailingTrivia(Node.GetTrailingTrivia()); // 如果右侧为空，返回左侧并保留后置琐碎内容

            if (Left is ExpressionSyntax LeftExpr && Right is ExpressionSyntax RightExpr) // 检查是否都是有效的表达式语法
            {
                return Node.Update(LeftExpr, Node.OperatorToken, RightExpr); // 更新二元表达式
            }

            return null; // 无法处理时返回 null
        }

        /// <summary>
        /// 处理三元表达式的简化。
        /// </summary>
        /// <param name="Node">待处理的三元表达式节点</param>
        /// <returns>简化后的表达式节点或 null</returns>
        private SyntaxNode? HandleTernary(ConditionalExpressionSyntax Node)
        {
            var Condition = Visit(Node.Condition);
            var TruePart = Visit(Node.WhenTrue);
            var FalsePart = Visit(Node.WhenFalse);

            if (Condition == null) return null;

            if (TruePart == null && FalsePart == null) return null;

            var NewTrue = TruePart is ExpressionSyntax T ? T : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            var NewFalse = FalsePart is ExpressionSyntax F ? F : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);

            if (Condition is ExpressionSyntax CondExpr)
            {
                return Node.Update(CondExpr, Node.QuestionToken, NewTrue, Node.ColonToken, NewFalse);
            }

            return null;
        }

        /// <summary>
        /// 处理括号表达式的简化。
        /// 示例：
        /// (a + b)，若内部简化为 a，则返回 a（自动去括号）。
        /// </summary>
        /// <param name="Node">待处理的括号表达式节点</param>
        /// <returns>简化后的表达式节点或 null</returns>
        private SyntaxNode? HandleParenthesized(ParenthesizedExpressionSyntax Node)
        {
            var Inner = Visit(Node.Expression);
            if (Inner == null) return null;
            if (Inner is IdentifierNameSyntax or LiteralExpressionSyntax) return Inner;
            return Node.WithExpression((ExpressionSyntax)Inner);
        }

        /// <summary>
        /// 处理前缀一元表达式。
        /// 示例：!a，若移除 a，则返回 null。
        /// </summary>
        /// <param name="Node">前缀一元表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandlePrefixUnary(PrefixUnaryExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Operand)) return null;
            var Operand = Visit(Node.Operand);
            if (Operand == null) return null;
            return Node.WithOperand((ExpressionSyntax)Operand);
        }

        /// <summary>
        /// 处理后缀一元表达式。
        /// 示例：a++，若移除 a，则返回 null。
        /// </summary>
        /// <param name="Node">后缀一元表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandlePostfixUnary(PostfixUnaryExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Operand)) return null;
            var Operand = Visit(Node.Operand);
            if (Operand == null) return null;
            return Node.WithOperand((ExpressionSyntax)Operand);
        }

        /// <summary>
        /// 处理 Await 表达式。
        /// 示例：await task，若移除 task，则返回 null。
        /// </summary>
        /// <param name="Node">Await 表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleAwait(AwaitExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理成员访问。
        /// 示例：obj.Member，若移除 obj，则返回 null（防止逻辑错误）。
        /// </summary>
        /// <param name="Node">成员访问节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleMemberAccess(MemberAccessExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Name)) return Visit(Node.Expression);

            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;

            if (Expression is ExpressionSyntax Expr)
            {
                return Node.Update(Expr, Node.OperatorToken, Node.Name);
            }
            return null;
        }

        /// <summary>
        /// 处理元素访问。
        /// 示例：arr[i]，若移除 arr，则返回 null。
        /// </summary>
        /// <param name="Node">元素访问节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleElementAccess(ElementAccessExpressionSyntax Node)
        {
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;

            var Args = Node.ArgumentList.Arguments
                .Select(a => (ArgumentSyntax?)Visit(a))
                .Where(a => a != null)
                .Cast<ArgumentSyntax>()
                .ToList();

            if (Args.Count < Node.ArgumentList.Arguments.Count && this.Model != null)
            {
                var OriginalElementAccess = GetOriginalNode(Node) as ElementAccessExpressionSyntax;
                var Symbol = OriginalElementAccess != null ? this.Model.GetSymbolInfo(OriginalElementAccess).Symbol as IPropertySymbol : null;
                var NewArgs = new System.Collections.Generic.List<ArgumentSyntax>();
                for (int i = 0; i < Node.ArgumentList.Arguments.Count; i++)
                {
                    var OriginalArg = Node.ArgumentList.Arguments[i];
                    var ProcessedArg = (ArgumentSyntax?)Visit(OriginalArg);
                    if (ProcessedArg != null)
                    {
                        NewArgs.Add(ProcessedArg);
                    }
                    else
                    {
                        ITypeSymbol? ParamType = null;
                        if (Symbol != null && Symbol.IsIndexer && i < Symbol.Parameters.Length)
                        {
                            ParamType = Symbol.Parameters[i].Type;
                        }

                        if (ParamType == null)
                        {
                            ParamType = GetNodeType(OriginalArg.Expression);
                        }

                        if (ParamType != null)
                        {
                            NewArgs.Add(OriginalArg.Update(OriginalArg.NameColon, OriginalArg.RefKindKeyword, CreatePlaceholder(ParamType)));
                        }
                        else
                        {
                            NewArgs.Add(OriginalArg.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
                        }
                    }
                }
                Args = NewArgs;
            }

            if (Args.Count == 0 && Node.ArgumentList.Arguments.Count > 0) return null;

            return Node.Update((ExpressionSyntax)Expression, Node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(Args)));
        }

        /// <summary>
        /// 处理方法调用。
        /// 示例：
        /// Method(a, b, c)，若移除 b，则变为 Method(a, c)。
        /// 若移除 Method，则返回 null。
        /// </summary>
        /// <param name="Node">方法调用节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleInvocation(InvocationExpressionSyntax Node)
        {
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;

            var Args = Node.ArgumentList.Arguments
                .Select(a => (ArgumentSyntax?)Visit(a))
                .Where(a => a != null)
                .Cast<ArgumentSyntax>()
                .ToList();

            if (Args.Count < Node.ArgumentList.Arguments.Count && this.Model != null)
            {
                var OriginalInvocation = GetOriginalNode(Node) as InvocationExpressionSyntax;
                var Symbol = OriginalInvocation != null ? this.Model.GetSymbolInfo(OriginalInvocation).Symbol as IMethodSymbol : null;
                var NewArgs = new System.Collections.Generic.List<ArgumentSyntax>();
                for (int i = 0; i < Node.ArgumentList.Arguments.Count; i++)
                {
                    var OriginalArg = Node.ArgumentList.Arguments[i];
                    var ProcessedArg = (ArgumentSyntax?)Visit(OriginalArg);
                    if (ProcessedArg != null)
                    {
                        NewArgs.Add(ProcessedArg);
                    }
                    else
                    {
                        ITypeSymbol? ParamType = null;
                        if (Symbol != null && i < Symbol.Parameters.Length)
                        {
                            ParamType = Symbol.Parameters[i].Type;
                        }

                        if (ParamType == null)
                        {
                            ParamType = GetNodeType(OriginalArg.Expression);
                        }

                        if (ParamType != null)
                        {
                            NewArgs.Add(OriginalArg.Update(OriginalArg.NameColon, OriginalArg.RefKindKeyword, CreatePlaceholder(ParamType)));
                        }
                        else
                        {
                            NewArgs.Add(OriginalArg.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
                        }
                    }
                }
                Args = NewArgs;
            }

            return Node.Update((ExpressionSyntax)Expression, Node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(Args)));
        }

        /// <summary>
        /// 处理条件访问（空合并访问）。
        /// 示例：obj?.Prop，若移除 obj，则返回 null。
        /// </summary>
        /// <param name="Node">条件访问节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleConditionalAccess(ConditionalAccessExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression) || this.ShouldRemove(Node.WhenNotNull)) return null;
            var Expression = Visit(Node.Expression);
            var WhenNotNull = Visit(Node.WhenNotNull);
            if (Expression == null || WhenNotNull == null) return null;
            return Node.Update((ExpressionSyntax)Expression, Node.OperatorToken, (ExpressionSyntax)WhenNotNull);
        }

        /// <summary>
        /// 处理初始化器。
        /// 示例：new List<int> { a, b }，若移除 a，则变为 { b }。
        /// </summary>
        /// <param name="Node">初始化器节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleInitializer(InitializerExpressionSyntax Node)
        {
            var Exprs = new System.Collections.Generic.List<ExpressionSyntax>();
            foreach (var E in Node.Expressions)
            {
                var Processed = (ExpressionSyntax?)Visit(E);
                if (Processed != null)
                {
                    Exprs.Add(Processed);
                }
                else
                {
                    // 如果在对象初始化器中，且是赋值表达式
                    if (Node.IsKind(SyntaxKind.ObjectInitializerExpression) && E is AssignmentExpressionSyntax Assign)
                    {
                        var Type = GetNodeType(Assign.Right);
                        if (Type != null)
                        {
                            Exprs.Add(Assign.WithRight(CreatePlaceholder(Type)));
                        }
                    }
                    else
                    {
                        var Type = GetNodeType(E);
                        if (Type != null)
                        {
                            Exprs.Add(CreatePlaceholder(Type));
                        }
                    }
                }
            }

            // 对于某些父节点，如果初始化器为空，则整个初始化器可以移除
            if (Exprs.Count == 0 && Node.Parent is not (ObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax)) return null;
            return Node.WithExpressions(SyntaxFactory.SeparatedList(Exprs)); // 更新并返回初始化器节点
        }

        /// <summary>
        /// 处理元组表达式。
        /// 示例：(a, b, c)，若移除 b，则变为 (a, c)。若只剩一个元素 a，则简化为 a。
        /// </summary>
        /// <param name="Node">元组表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleTuple(TupleExpressionSyntax Node)
        {
            var Elements = new System.Collections.Generic.List<ArgumentSyntax>();
            foreach (var Arg in Node.Arguments)
            {
                var Processed = (ArgumentSyntax?)Visit(Arg);
                if (Processed != null)
                {
                    Elements.Add(Processed);
                }
                else
                {
                    var Type = GetNodeType(Arg.Expression);
                    if (Type != null)
                    {
                        Elements.Add(Arg.WithExpression(CreatePlaceholder(Type)));
                    }
                }
            }

            if (Elements.Count == 0 && Node.Arguments.Count > 0) return null;
            if (Elements.Count == 1) return Elements[0].Expression;
            return Node.WithArguments(SyntaxFactory.SeparatedList(Elements));
        }

        /// <summary>
        /// 处理集合表达式。
        /// 示例：[a, b, c]，若移除 b，则变为 [a, c]。
        /// </summary>
        /// <param name="Node">集合表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleCollection(CollectionExpressionSyntax Node)
        {
            var Elements = Node.Elements // 获取集合元素列表
                .Select(e => (CollectionElementSyntax?)Visit(e)) // 访问并重写每个元素
                .Where(e => e != null) // 过滤掉被移除的元素
                .Cast<CollectionElementSyntax>() // 转换为 CollectionElementSyntax
                .ToList(); // 转换为列表
            return Node.WithElements(SyntaxFactory.SeparatedList(Elements)); // 更新并返回带新元素列表的集合节点
        }

        /// <summary>
        /// 访问表达式元素节点。
        /// </summary>
        /// <param name="Node">表达式元素节点</param>
        /// <returns>重写后的表达式元素节点</returns>
        public override SyntaxNode? VisitExpressionElement(ExpressionElementSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression); // 访问并重写内部表达式
            if (Expression == null) return null; // 如果表达式被移除，则元素也被移除
            return Node.WithExpression(Expression); // 更新并返回表达式元素节点
        }

        /// <summary>
        /// 访问展开元素节点（如 ..collection）。
        /// </summary>
        /// <param name="Node">展开元素节点</param>
        /// <returns>重写后的展开元素节点</returns>
        public override SyntaxNode? VisitSpreadElement(SpreadElementSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression); // 访问并重写展开操作的目标表达式
            if (Expression == null) return null; // 如果表达式被移除，则展开元素也被移除
            return Node.WithExpression(Expression); // 更新并返回展开元素节点
        }

        /// <summary>
        /// 访问 If 语句。
        /// 若条件表达式被移除，则整个 If 语句移除。
        /// 若 If 分支被移除但存在 Else 分支，则尝试将 Else 分支提升。
        /// </summary>
        /// <param name="Node">If 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax Node)
        {
            var Condition = (ExpressionSyntax?)Visit(Node.Condition);
            if (Condition == null)
            {
                return null;
            }

            var Statement = (StatementSyntax?)Visit(Node.Statement);
            var ElseClause = (ElseClauseSyntax?)Visit(Node.Else);

            // 如果 If 分支为空（原先是 BlockSyntax 但现在没有语句了），将其视为 null
            if (Statement is BlockSyntax Block && Block.Statements.Count == 0)
            {
                Statement = null;
            }

            if (Statement == null)
            {
                // 如果 If 分支被移除，但有 Else 分支，则尝试将 Else 分支提升
                if (ElseClause != null)
                {
                    var ElseStmt = ElseClause.Statement;
                    if (ElseStmt is BlockSyntax ElseBlock && ElseBlock.Statements.Count == 1)
                    {
                        return ElseBlock.Statements[0].WithLeadingTrivia(Node.GetLeadingTrivia());
                    }
                    return ElseStmt.WithLeadingTrivia(Node.GetLeadingTrivia());
                }
                // 如果都没有，则整个 If 也没有了
                return null;
            }

            return Node.Update(Node.IfKeyword, Node.OpenParenToken, Condition, Node.CloseParenToken, Statement, ElseClause);
        }

        /// <summary>
        /// 访问 Else 子句。
        /// 若内部语句被移除，则整个 Else 子句移除。
        /// </summary>
        /// <param name="Node">Else 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitElseClause(ElseClauseSyntax Node)
        {
            var Statement = (StatementSyntax?)Visit(Node.Statement);
            if (Statement == null) return null;
            return Node.Update(Node.ElseKeyword, Statement);
        }

        /// <summary>
        /// 访问表达式语句。
        /// 若内部表达式被移除，或者简化后的表达式变为无副作用的字面量或标识符，则移除整个语句。
        /// </summary>
        /// <param name="Node">表达式语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null) return null;

            // 如果内部表达式变成了无意义的字面量或标识符，则移除整个语句
            if (Expression is LiteralExpressionSyntax || Expression is IdentifierNameSyntax) return null;

            return Node.Update(Expression, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问 While 语句。
        /// 若条件表达式或循环体被移除，则整个 While 语句移除。
        /// </summary>
        /// <param name="Node">While 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax Node)
        {
            var Condition = (ExpressionSyntax?)Visit(Node.Condition);
            var Statement = (StatementSyntax?)Visit(Node.Statement);

            if (Condition == null || Statement == null)
            {
                return null;
            }
            return Node.Update(Node.WhileKeyword, Node.OpenParenToken, Condition, Node.CloseParenToken, Statement);
        }

        /// <summary>
        /// 访问 Switch 语句。
        /// 若控制表达式被移除，则整个 Switch 语句移除；同时简化并更新各 Case 分节。
        /// </summary>
        /// <param name="Node">Switch 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null)
            {
                return null;
            }

            var Sections = Node.Sections
                .Select(s => (SwitchSectionSyntax?)Visit(s))
                .Where(s => s != null)
                .Cast<SwitchSectionSyntax>()
                .ToList();

            return Node.Update(Node.SwitchKeyword, Node.OpenParenToken, Expression, Node.CloseParenToken, Node.OpenBraceToken, SyntaxFactory.List(Sections), Node.CloseBraceToken);
        }

        /// <summary>
        /// 访问 Return 语句。
        /// 简化返回表达式。若表达式被移除且函数有非 void 返回值，则生成占位符。
        /// </summary>
        /// <param name="Node">Return 语句节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);

            // 如果原先有表达式但简化后被移除了，需要根据函数返回类型补上占位符
            if (Node.Expression != null && Expression == null && this.Model != null)
            {
                var OriginalNode = GetOriginalNode(Node);
                if (OriginalNode != null)
                {
                    var Symbol = this.Model.GetEnclosingSymbol(OriginalNode.SpanStart);
                    ITypeSymbol? ReturnType = null;
                    if (Symbol is IMethodSymbol Method)
                    {
                        ReturnType = Method.ReturnType;
                    }
                    else if (Symbol is IPropertySymbol Property)
                    {
                        ReturnType = Property.Type;
                    }

                    if (ReturnType != null && ReturnType.SpecialType != SpecialType.System_Void)
                    {
                        Expression = CreatePlaceholder(ReturnType);
                    }
                }
            }

            return Node.Update(Node.ReturnKeyword, Expression, Node.SemicolonToken);
        }

        /// <summary>
        /// 创建指定类型的占位符表达式。
        /// </summary>
        private ExpressionSyntax CreatePlaceholder(ITypeSymbol Type)
        {
            var TypeName = Type.ToDisplayString();
            return SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(TypeName));
        }

        /// <summary>
        /// 访问局部变量声明语句。
        /// 简化内部的变量声明部分。若变量声明被完全移除，则整个语句也随之移除。
        /// </summary>
        /// <param name="Node">局部变量声明语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax Node)
        {
            var Declaration = (VariableDeclarationSyntax?)Visit(Node.Declaration);
            if (Declaration == null) return null;
            return Node.Update(Node.AttributeLists, Node.AwaitKeyword, Node.UsingKeyword, Node.Modifiers, Declaration, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问变量声明。
        /// 简化变量类型和变量声明器列表。若类型被移除或所有声明器都被移除，则整个变量声明失效。
        /// </summary>
        /// <param name="Node">变量声明节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax Node)
        {
            var Type = (TypeSyntax?)Visit(Node.Type);
            var Variables = Node.Variables
                .Select(v => (VariableDeclaratorSyntax?)Visit(v))
                .Where(v => v != null)
                .Cast<VariableDeclaratorSyntax>()
                .ToList();

            if (Type == null || Variables.Count == 0) return null;
            return Node.Update(Type, SyntaxFactory.SeparatedList(Variables));
        }

        /// <summary>
        /// 访问等号值子句（变量初始化）。
        /// 简化初始化表达式。若表达式被移除，则整个等号值子句移除。
        /// </summary>
        /// <param name="Node">等号值子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitEqualsValueClause(EqualsValueClauseSyntax Node)
        {
            var Value = (ExpressionSyntax?)Visit(Node.Value);
            if (Value == null) return null;
            return Node.Update(Node.EqualsToken, Value);
        }

        /// <summary>
        /// 访问变量声明器。
        /// 简化初始化器。若原先存在初始化器但简化后被移除，则该变量声明器也应被移除。
        /// </summary>
        /// <param name="Node">变量声明器节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax Node)
        {
            var Initializer = (EqualsValueClauseSyntax?)Visit(Node.Initializer);
            // 如果原先有初始化表达式，但简化后被移除了，则该变量声明也应被移除
            if (Node.Initializer != null && Initializer == null)
            {
                return null;
            }
            return Node.Update(Node.Identifier, Node.ArgumentList, Initializer);
        }

        /// <summary>
        /// 访问 Catch 子句。
        /// 简化 Catch 块。若 Catch 块中的语句被全部移除且无 Filter，则移除整个 Catch 子句。
        /// </summary>
        /// <param name="Node">Catch 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitCatchClause(CatchClauseSyntax Node)
        {
            var Declaration = (CatchDeclarationSyntax?)Visit(Node.Declaration);
            var Filter = (CatchFilterClauseSyntax?)Visit(Node.Filter);
            var Block = (BlockSyntax?)Visit(Node.Block);

            if (Block == null) return null;

            return Node.Update(Node.CatchKeyword, Declaration, Filter, Block);
        }

        /// <summary>
        /// 访问 ForEach 语句。
        /// 简化集合表达式和循环体。若任意核心部分被移除，则移除整个 ForEach 语句。
        /// </summary>
        /// <param name="Node">ForEach 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            var Statement = (StatementSyntax?)Visit(Node.Statement);
            var Type = (TypeSyntax?)Visit(Node.Type);

            if (Expression == null || Statement == null || Type == null)
            {
                return null;
            }

            return Node.Update(Node.AttributeLists, Node.AwaitKeyword, Node.ForEachKeyword, Node.OpenParenToken, Type, Node.Identifier, Node.InKeyword, Expression, Node.CloseParenToken, Statement);
        }

        /// <summary>
        /// 访问 For 语句。
        /// 简化初始化、条件、增量和循环体。若原先存在的头部组件简化后消失，则移除整个 For 语句。
        /// </summary>
        /// <param name="Node">For 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitForStatement(ForStatementSyntax Node)
        {
            var Declaration = (VariableDeclarationSyntax?)Visit(Node.Declaration);
            var Initializers = VisitList(Node.Initializers);
            var Condition = (ExpressionSyntax?)Visit(Node.Condition);
            var Incrementors = VisitList(Node.Incrementors);
            var Statement = (StatementSyntax?)Visit(Node.Statement);

            // 如果初始部分、条件部分或增量部分原先存在但现在被移除了，则整个 for 循环失效
            bool HeaderRemoved = (Node.Declaration != null && Declaration == null) ||
                                 (Node.Condition != null && Condition == null) ||
                                 (Node.Initializers.Count > 0 && Initializers.Count == 0) ||
                                 (Node.Incrementors.Count > 0 && Incrementors.Count == 0);

            if (HeaderRemoved || Statement == null)
            {
                return null;
            }

            return Node.Update(Node.ForKeyword, Node.OpenParenToken, Declaration, Initializers, Node.FirstSemicolonToken, Condition, Node.SecondSemicolonToken, Incrementors, Node.CloseParenToken, Statement);
        }

        /// <summary>
        /// 访问 Do 语句。
        /// 简化循环体和条件。若任意部分被移除，则移除整个 Do 语句。
        /// </summary>
        /// <param name="Node">Do 语句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitDoStatement(DoStatementSyntax Node)
        {
            var Statement = (StatementSyntax?)Visit(Node.Statement);
            var Condition = (ExpressionSyntax?)Visit(Node.Condition);

            if (Statement == null || Condition == null)
            {
                return null;
            }

            return Node.Update(Node.DoKeyword, Statement, Node.WhileKeyword, Node.OpenParenToken, Condition, Node.CloseParenToken, Node.SemicolonToken);
        }

        /// <summary>
        /// 访问参数节点。
        /// 若参数标记为移除，则尝试生成占位符（如 default(T)）；若无法生成，则移除参数。
        /// </summary>
        /// <param name="Node">参数节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitArgument(ArgumentSyntax Node)
        {
            if (this.ShouldRemove(Node))
            {
                var Placeholder = TryGetPlaceholderExpression(Node.Expression);
                return Placeholder != null ? Node.WithExpression(Placeholder) : null;
            }
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null)
            {
                var Placeholder = TryGetPlaceholderExpression(Node.Expression);
                return Placeholder != null ? Node.WithExpression(Placeholder) : null;
            }
            return Node.Update(Node.NameColon, Node.RefKindKeyword, Expression);
        }

        /// <summary>
        /// 访问特性节点。
        /// 更新特性的参数列表。
        /// </summary>
        /// <param name="Node">特性节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitAttribute(AttributeSyntax Node)
        {
            var ArgumentList = (AttributeArgumentListSyntax?)Visit(Node.ArgumentList);
            // 这里不要显式返回 node.WithArgumentList(null)，让 VisitAttributeArgumentList 处理
            return Node.Update(Node.Name, ArgumentList);
        }

        /// <summary>
        /// 访问特性参数节点。
        /// 若特性参数标记为移除，则尝试用占位符替换，以保持特性构造函数的参数结构。
        /// </summary>
        /// <param name="Node">特性参数节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitAttributeArgument(AttributeArgumentSyntax Node)
        {
            if (this.ShouldRemove(Node))
            {
                var Placeholder = TryGetPlaceholderExpression(Node.Expression);
                return Placeholder != null ? Node.WithExpression(Placeholder) : null;
            }
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null)
            {
                var Placeholder = TryGetPlaceholderExpression(Node.Expression);
                return Placeholder != null ? Node.WithExpression(Placeholder) : null;
            }
            return Node.Update(Node.NameEquals, Node.NameColon, Expression);
        }

        /// <summary>
        /// 访问特性参数列表。
        /// 遍历参数，移除标记为删除的参数并尽可能用占位符填充，以确保特性语法合法。
        /// </summary>
        /// <param name="Node">特性参数列表节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitAttributeArgumentList(AttributeArgumentListSyntax Node)
        {
            var NewArgs = new System.Collections.Generic.List<AttributeArgumentSyntax>();
            foreach (var Arg in Node.Arguments)
            {
                var Processed = (AttributeArgumentSyntax?)Visit(Arg);
                if (Processed != null)
                {
                    NewArgs.Add(Processed);
                }
                else
                {
                    // 如果属性参数被移除，生成占位符来保持参数列表完整
                    var Placeholder = TryGetPlaceholderExpression(Arg.Expression);
                    if (Placeholder != null)
                    {
                        NewArgs.Add(Arg.WithExpression(Placeholder));
                    }
                }
            }

            if (NewArgs.Count == 0 && Node.Arguments.Count > 0)
            {
                // 如果是受保护的列表，尝试为每个原始参数生成占位符
                foreach (var Arg in Node.Arguments)
                {
                    var Placeholder = TryGetPlaceholderExpression(Arg.Expression);
                    if (Placeholder != null)
                    {
                        NewArgs.Add(Arg.WithExpression(Placeholder));
                    }
                    else
                    {
                        // 实在不行，用 null 占位
                        NewArgs.Add(Arg.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
                    }
                }
            }

            if (NewArgs.Count == 0 && Node.Arguments.Count > 0) return null;
            return Node.WithArguments(SyntaxFactory.SeparatedList(NewArgs));
        }

        /// <summary>
        /// 访问普通参数列表。
        /// 遍历参数，移除标记为删除的参数并尽可能用占位符填充，以保持函数调用的参数结构。
        /// </summary>
        /// <param name="Node">参数列表节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitArgumentList(ArgumentListSyntax Node)
        {
            var NewArgs = new System.Collections.Generic.List<ArgumentSyntax>();
            for (int i = 0; i < Node.Arguments.Count; i++)
            {
                var Arg = Node.Arguments[i];
                var Processed = (ArgumentSyntax?)Visit(Arg);
                if (Processed != null)
                {
                    NewArgs.Add(Processed);
                }
                else
                {
                    var Placeholder = TryGetPlaceholderExpression(Arg.Expression);
                    if (Placeholder != null)
                    {
                        NewArgs.Add(Arg.WithExpression(Placeholder));
                    }
                }
            }

            if (NewArgs.Count == 0 && Node.Arguments.Count > 0) return null;
            return Node.WithArguments(SyntaxFactory.SeparatedList(NewArgs));
        }

        /// <summary>
        /// 访问元组表达式。
        /// 遍历元组元素，若元素被移除，则用占位符（default(T)）替换，以保持元组的维度（arity）。
        /// </summary>
        /// <param name="Node">元组表达式节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitTupleExpression(TupleExpressionSyntax Node)
        {
            var NewArgs = new System.Collections.Generic.List<ArgumentSyntax>();
            foreach (var Arg in Node.Arguments)
            {
                var Processed = (ArgumentSyntax?)Visit(Arg);
                if (Processed != null)
                {
                    NewArgs.Add(Processed);
                }
                else
                {
                    var Type = GetNodeType(Arg.Expression);
                    if (Type != null)
                    {
                        NewArgs.Add(Arg.WithExpression(CreatePlaceholder(Type)));
                    }
                }
            }

            if (NewArgs.Count == 0 && Node.Arguments.Count > 0) return null;
            return Node.WithArguments(SyntaxFactory.SeparatedList(NewArgs));
        }

        /// <summary>
        /// 处理对象创建。
        /// 示例：new MyClass(a, b)，若移除 a，则变为 new MyClass(b)。
        /// </summary>
        /// <param name="Node">对象创建节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleBaseObjectCreation(BaseObjectCreationExpressionSyntax Node)
        {
            TypeSyntax? Type = null;
            if (Node is ObjectCreationExpressionSyntax ObjCreate)
            {
                if (this.ShouldRemove(ObjCreate.Type)) return null;
                Type = (TypeSyntax?)Visit(ObjCreate.Type);
                if (Type == null) return null;
            }

            var Args = Node.ArgumentList?.Arguments
                .Select(a => (ArgumentSyntax?)Visit(a))
                .Where(a => a != null)
                .Cast<ArgumentSyntax>()
                .ToList();

            var OriginalNode = GetOriginalNode(Node) as BaseObjectCreationExpressionSyntax;
            if (Node.ArgumentList != null && Args != null && Args.Count < Node.ArgumentList.Arguments.Count && this.Model != null && OriginalNode != null)
            {
                var Symbol = this.Model.GetSymbolInfo(OriginalNode).Symbol as IMethodSymbol;
                var NewArgs = new System.Collections.Generic.List<ArgumentSyntax>();
                for (int i = 0; i < Node.ArgumentList.Arguments.Count; i++)
                {
                    var OriginalArg = Node.ArgumentList.Arguments[i];
                    var ProcessedArg = (ArgumentSyntax?)Visit(OriginalArg);
                    if (ProcessedArg != null)
                    {
                        NewArgs.Add(ProcessedArg);
                    }
                    else
                    {
                        ITypeSymbol? ParamType = null;
                        if (Symbol != null && i < Symbol.Parameters.Length)
                        {
                            ParamType = Symbol.Parameters[i].Type;
                        }

                        var OriginalArgInTree = GetOriginalNode(OriginalArg) as ArgumentSyntax;
                        if (ParamType == null && OriginalArgInTree != null)
                        {
                            var ArgTypeInfo = this.Model.GetTypeInfo(OriginalArgInTree.Expression);
                            ParamType = ArgTypeInfo.ConvertedType ?? ArgTypeInfo.Type;
                        }

                        if (ParamType != null)
                        {
                            NewArgs.Add(OriginalArg.Update(OriginalArg.NameColon, OriginalArg.RefKindKeyword, CreatePlaceholder(ParamType)));
                        }
                        else
                        {
                            NewArgs.Add(OriginalArg.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
                        }
                    }
                }
                Args = NewArgs;
            }

            var Initializer = (InitializerExpressionSyntax?)Visit(Node.Initializer);
            var NewArgList = Node.ArgumentList != null ? Node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(Args)) : null;

            if (Node is ObjectCreationExpressionSyntax OC)
            {
                return OC.Update(OC.NewKeyword, Type!, NewArgList, Initializer);
            }
            if (Node is ImplicitObjectCreationExpressionSyntax IOC)
                return IOC.WithArgumentList(NewArgList!).WithInitializer(Initializer);

            return base.Visit(Node);
        }

        /// <summary>
        /// 处理数组类型。
        /// 示例：int[]，若移除 int，则返回 null。
        /// </summary>
        /// <param name="Node">数组类型节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleArrayType(ArrayTypeSyntax Node)
        {
            var ElementType = Visit(Node.ElementType);
            if (ElementType == null) return null;
            var RankSpecifiers = Node.RankSpecifiers
                .Select(r => (ArrayRankSpecifierSyntax?)Visit(r))
                .Where(r => r != null)
                .Cast<ArrayRankSpecifierSyntax>()
                .ToList();
            if (RankSpecifiers.Count == 0 && Node.RankSpecifiers.Count > 0) return null;

            return Node.Update((TypeSyntax)ElementType, SyntaxFactory.List(RankSpecifiers));
        }

        /// <summary>
        /// 处理数组创建。
        /// 示例：new int[5]，若移除 int 类型，则返回 null。
        /// </summary>
        /// <param name="Node">数组创建节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleArrayCreation(ArrayCreationExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Type)) return null;
            if (Node.Initializer != null && this.ShouldRemove(Node.Initializer)) return null;
            var Type = (ArrayTypeSyntax?)Visit(Node.Type);
            var Initializer = Node.Initializer != null ? (InitializerExpressionSyntax?)Visit(Node.Initializer) : null;
            if (Type == null) return null;
            if (Node.Initializer != null && Initializer == null) return null;
            return Node.Update(Node.NewKeyword, Type, Initializer);
        }

        /// <summary>
        /// 处理隐式数组创建。
        /// 示例：new[] { a, b }，若移除初始化器，则返回 null。
        /// </summary>
        /// <param name="Node">隐式数组创建节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Initializer)) return null;
            var Initializer = (InitializerExpressionSyntax?)Visit(Node.Initializer);
            if (Initializer == null) return null;
            return Node.WithInitializer(Initializer);
        }

        /// <summary>
        /// 处理 stackalloc 数组。
        /// 示例：stackalloc int[5]，若移除 int 类型，则返回 null。
        /// </summary>
        /// <param name="Node">stackalloc 数组创建节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleStackAllocArrayCreation(StackAllocArrayCreationExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Type)) return null;
            var Type = Visit(Node.Type);
            if (Type == null) return null;
            return Node.WithType((TypeSyntax)Type);
        }

        /// <summary>
        /// 处理隐式 stackalloc 数组。
        /// 示例：stackalloc[] { a, b }，若移除初始化器，则返回 null。
        /// </summary>
        /// <param name="Node">隐式 stackalloc 数组创建节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleImplicitStackAllocArrayCreation(ImplicitStackAllocArrayCreationExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Initializer)) return null;
            var Initializer = (InitializerExpressionSyntax?)Visit(Node.Initializer);
            if (Initializer == null) return null;
            return Node.WithInitializer(Initializer);
        }

        /// <summary>
        /// 处理类型转换。示例：(int)a，若移除 a，则返回 null；若移除 int，则返回 a。
        /// </summary>
        /// <param name="Node">类型转换节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleCast(CastExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            if (this.ShouldRemove(Node.Type)) return Visit(Node.Expression);
            var Type = Visit(Node.Type);
            var Expression = Visit(Node.Expression);
            if (Type == null || Expression == null) return null;
            return Node.Update(Node.OpenParenToken, (TypeSyntax)Type, Node.CloseParenToken, (ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理 typeof。示例：typeof(T)，若移除 T，则返回 null。
        /// </summary>
        /// <param name="Node">typeof 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleTypeOf(TypeOfExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Type)) return null;
            var Type = Visit(Node.Type);
            if (Type == null) return null;
            return Node.WithType((TypeSyntax)Type);
        }

        /// <summary>
        /// 处理模式匹配。示例：e is P，若移除 e 或 P，则返回 null。
        /// </summary>
        /// <param name="Node">模式匹配节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleIsPattern(IsPatternExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression) || this.ShouldRemove(Node.Pattern)) return null;
            var Expression = Visit(Node.Expression);
            var Pattern = (PatternSyntax?)Visit(Node.Pattern);
            if (Expression == null || Pattern == null) return null;
            return Node.Update((ExpressionSyntax)Expression, Node.IsKeyword, Pattern);
        }

        /// <summary>
        /// 处理 __reftype。
        /// 示例：__reftype(typedRef)，若移除 typedRef，则返回 null。
        /// </summary>
        /// <param name="Node">__reftype 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleRefType(RefTypeExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理 sizeof。示例：sizeof(int)，若移除 int，则返回 null。
        /// </summary>
        /// <param name="Node">sizeof 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleSizeOf(SizeOfExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Type)) return null;
            var Type = Visit(Node.Type);
            if (Type == null) return null;
            return Node.WithType((TypeSyntax)Type);
        }

        /// <summary>
        /// 处理通用类型语法。
        /// 示例：List<int>，若移除整个类型 Node，则返回 null。
        /// </summary>
        /// <param name="Node">类型语法节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleType(TypeSyntax Node) => this.ShouldRemove(Node) ? null : base.Visit(Node);

        /// <summary>
        /// 处理赋值表达式。
        /// 简化左侧和右侧操作数。若为复合赋值（如 +=），左侧必须保留；若为简单赋值（=），则根据左右侧简化情况决定是否移除或保留其中一方。
        /// 若左侧被移除，通常整个赋值失效；若右侧被移除，则尝试用占位符填充。
        /// </summary>
        /// <param name="Node">赋值表达式节点</param>
        /// <returns>简化后的节点或 null</returns>
        private SyntaxNode? HandleAssignment(AssignmentExpressionSyntax Node)
        {
            if (Node == null) return null;
            var Left = Visit(Node.Left);
            var Right = Visit(Node.Right);

            if (Left == null && Right == null) return null;

            if (Node.Kind() != SyntaxKind.SimpleAssignmentExpression)
            {
                if (Left == null) return null;
                if (Right == null) return Left is SyntaxNode leftNode ? leftNode.WithTrailingTrivia(Node.GetTrailingTrivia()) : null;
            }
            else
            {
                if (Left == null)
                {
                    // 上下文感知模式：
                    // 如果是在表达式语句上下文中（作为独立行），则直接移除整行
                    if (Node.Parent is ExpressionStatementSyntax) return null;

                    // 如果是在需要值的上下文中（如 M(x = 1) 或 return x = 1），则保留右侧的值
                    return Right;
                }
                if (Right == null)
                {
                    // 如果右侧被移除，优先检查是否可以全删（针对局部变量/参数的简单赋值）
                    bool IsLocalOrParam = false;
                    var OriginalLeft = GetOriginalNode(Node.Left);
                    if (this.Model != null && OriginalLeft != null)
                    {
                        var Symbol = this.Model.GetSymbolInfo(OriginalLeft).Symbol;
                        if (Symbol != null && (Symbol.Kind == SymbolKind.Local || Symbol.Kind == SymbolKind.Parameter))
                        {
                            IsLocalOrParam = true;
                        }
                    }

                    if (IsLocalOrParam || this.Model == null)
                    {
                        return null;
                    }

                    // 对于非局部变量（如字段/属性），为了保持编译通过，尝试使用占位符
                    var OriginalRight = GetOriginalNode(Node.Right);
                    if (this.Model != null && OriginalRight != null)
                    {
                        var TypeInfo = this.Model.GetTypeInfo(OriginalRight);
                        var ArgType = TypeInfo.ConvertedType ?? TypeInfo.Type;
                        if (ArgType != null)
                        {
                            var placeholder = CreatePlaceholder(ArgType);
                            if (placeholder != null)
                            {
                                if (Left is ExpressionSyntax leftExpr)
                                {
                                    return Node.Update(leftExpr, Node.OperatorToken, placeholder);
                                }
                            }
                        }
                    }

                    return null;
                }
            }

            if (Left is ExpressionSyntax LeftExpr && Right is ExpressionSyntax RightExpr)
            {
                return Node.Update(LeftExpr, Node.OperatorToken, RightExpr);
            }
            return Left ?? Right;
        }

        /// <summary>
        /// 处理声明表达式。示例：out var x，若移除类型，则返回 null。
        /// </summary>
        /// <param name="Node">声明表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleDeclaration(DeclarationExpressionSyntax Node)
        {
            if (Node == null) return null;
            var Type = Node.Type != null ? (TypeSyntax?)Visit(Node.Type) : null;
            var Designation = Node.Designation != null ? (VariableDesignationSyntax?)Visit(Node.Designation) : null;

            if (Designation == null)
            {
                return null;
            }

            if (Type == null && Node.Type != null)
            {
                // 如果类型被移除但不是 var，则可能需要处理
                return Designation;
            }

            if (Type == null)
            {
                Type = SyntaxFactory.IdentifierName("var");
            }

            return Node.Update(Type, Designation);
        }

        /// <summary>
        /// 处理 throw 表达式。示例：throw ex，若移除 ex，则返回 null。
        /// </summary>
        /// <param name="Node">throw 表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleThrow(ThrowExpressionSyntax Node)
        {
            if (Node.Expression != null && this.ShouldRemove(Node.Expression)) return null;
            if (Node.Expression == null) return base.VisitThrowExpression(Node);
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理匿名函数。
        /// 示例：(x) => body，若移除 body，则返回 null。
        /// </summary>
        /// <param name="Node">匿名函数节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleAnonymousFunction(AnonymousFunctionExpressionSyntax Node)
        {
            var Body = Visit(Node.Body);
            if (Body == null)
            {
                if (Node.Body is BlockSyntax)
                {
                    Body = SyntaxFactory.Block();
                }
                else
                {
                    Body = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                }
            }

            if (Node is ParenthesizedLambdaExpressionSyntax PL)
                return PL.WithBody((CSharpSyntaxNode)Body);
            if (Node is SimpleLambdaExpressionSyntax SL)
                return SL.WithBody((CSharpSyntaxNode)Body);
            if (Node is AnonymousMethodExpressionSyntax AM)
                return AM.WithBody((BlockSyntax)Body);

            return base.Visit(Node);
        }

        /// <summary>
        /// 处理 Switch 表达式.
        /// 示例：
        /// e switch { p => r }，若移除 e，则返回 null。
        /// 若移除某个 arm (p => r)，则从列表中移除。
        /// </summary>
        /// <param name="Node">Switch 表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleSwitch(SwitchExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.GoverningExpression)) return null;
            var Governing = Visit(Node.GoverningExpression);
            if (Governing == null) return null;

            var Arms = Node.Arms
                .Select(a => (SwitchExpressionArmSyntax?)Visit(a))
                .Where(a => a != null)
                .Cast<SwitchExpressionArmSyntax>()
                .ToList();

            return Node.Update((ExpressionSyntax)Governing, Node.SwitchKeyword, Node.OpenBraceToken, SyntaxFactory.SeparatedList(Arms), Node.CloseBraceToken);
        }

        /// <summary>
    /// 访问 Switch 表达式分支（Arm）。
    /// 简化模式和表达式。若表达式被移除，则尝试根据返回类型生成占位符；若无法生成或模式被移除，则移除整个分支。
    /// </summary>
        /// <param name="Node">Switch 表达式分支节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitSwitchExpressionArm(SwitchExpressionArmSyntax Node)
        {
            var Pattern = Visit(Node.Pattern);
            var WhenClause = Node.WhenClause != null ? (WhenClauseSyntax?)Visit(Node.WhenClause) : null;
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);

            if (Pattern == null) return null;

            if (Expression == null && this.Model != null)
            {
                var Type = GetNodeType(Node.Expression);
                if (Type != null)
                {
                    Expression = CreatePlaceholder(Type);
                }
            }

            if (Expression == null) return null;

            return Node.Update((PatternSyntax)Pattern, WhenClause, Node.EqualsGreaterThanToken, Expression);
        }

        /// <summary>
        /// 访问 when 子句。
        /// </summary>
        /// <param name="Node">when 子句节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitWhenClause(WhenClauseSyntax Node)
        {
            var Condition = (ExpressionSyntax?)Visit(Node.Condition);
            if (Condition == null) return null;
            return Node.WithCondition(Condition);
        }

        /// <summary>
        /// 访问 Select 子句。
        /// </summary>
        /// <param name="Node">Select 子句节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitSelectClause(SelectClauseSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression(Expression);
        }

        /// <summary>
        /// 访问 Group 子句。
        /// </summary>
        /// <param name="Node">Group 子句节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitGroupClause(GroupClauseSyntax Node)
        {
            var GroupExpression = (ExpressionSyntax?)Visit(Node.GroupExpression);
            if (GroupExpression == null) return null;

            var ByExpression = (ExpressionSyntax?)Visit(Node.ByExpression);
            if (ByExpression == null) return null;

            return Node.Update(Node.GroupKeyword, GroupExpression, Node.ByKeyword, ByExpression);
        }

        /// <summary>
        /// 访问查询体。
        /// 简化查询子句和选择/分组子句。若选择/分组子句被移除，则整个查询体失效。
        /// </summary>
        /// <param name="Node">查询体节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitQueryBody(QueryBodySyntax Node)
        {
            var SelectOrGroup = (SelectOrGroupClauseSyntax?)Visit(Node.SelectOrGroup);
            if (SelectOrGroup == null) return null;

            var Clauses = Node.Clauses
                .Select(c => (QueryClauseSyntax?)Visit(c))
                .Where(c => c != null)
                .Cast<QueryClauseSyntax>()
                .ToList();
            var Continuation = Node.Continuation != null ? (QueryContinuationSyntax?)Visit(Node.Continuation) : null;

            return Node.Update(SyntaxFactory.List(Clauses), SelectOrGroup, Continuation);
        }

        /// <summary>
        /// 访问 From 子句。
        /// 简化源表达式。若源表达式被移除，则 From 子句失效。
        /// </summary>
        /// <param name="Node">From 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitFromClause(FromClauseSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression(Expression);
        }

        /// <summary>
        /// 访问 Let 子句。
        /// 简化初始化表达式。若表达式被移除，则 Let 子句失效。
        /// </summary>
        /// <param name="Node">Let 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitLetClause(LetClauseSyntax Node)
        {
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 访问 Where 子句。
        /// 简化过滤条件。若条件被移除，则 Where 子句失效。
        /// </summary>
        /// <param name="Node">Where 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitWhereClause(WhereClauseSyntax Node)
        {
            var Condition = Visit(Node.Condition);
            if (Condition == null) return null;
            return Node.WithCondition((ExpressionSyntax)Condition);
        }

        /// <summary>
        /// 访问 Join 子句。
        /// 简化连接集合表达式。若集合表达式被移除，则 Join 子句失效。
        /// </summary>
        /// <param name="Node">Join 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitJoinClause(JoinClauseSyntax Node)
        {
            var InExpression = (ExpressionSyntax?)Visit(Node.InExpression);
            if (InExpression == null) return null;
            return Node.WithInExpression(InExpression);
        }

        /// <summary>
        /// 访问 OrderBy 子句。
        /// 简化排序项列表。若所有排序项都被移除，则 OrderBy 子句失效。
        /// </summary>
        /// <param name="Node">OrderBy 子句节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitOrderByClause(OrderByClauseSyntax Node)
        {
            var Orderings = Node.Orderings
                .Select(o => (OrderingSyntax?)Visit(o))
                .Where(o => o != null)
                .Cast<OrderingSyntax>()
                .ToList();
            if (Orderings.Count == 0) return null;
            return Node.WithOrderings(SyntaxFactory.SeparatedList(Orderings));
        }

        /// <summary>
        /// 访问排序项。
        /// 简化排序键表达式。若表达式被移除，则排序项失效。
        /// </summary>
        /// <param name="Node">排序项节点</param>
        /// <returns>简化后的节点或 null</returns>
        public override SyntaxNode? VisitOrdering(OrderingSyntax Node)
        {
            var Expression = (ExpressionSyntax?)Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression(Expression);
        }

        /// <summary>
        /// 处理 with 表达式。
        /// 示例：
        /// obj with { P = v }，若移除 obj，则返回 null。
        /// 若移除 Initializer，则返回 obj。
        /// </summary>
        /// <param name="Node">with 表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleWith(WithExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;

            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            if (this.ShouldRemove(Node.Initializer)) return Expression;
            var Initializer = Visit(Node.Initializer);
            if (Initializer == null) return Expression;

            return Node.Update((ExpressionSyntax)Expression, Node.WithKeyword, (InitializerExpressionSyntax)Initializer);
        }

        /// <summary>
        /// 处理范围表达式。
        /// 示例：
        /// ..5，若移除 0，则返回 ..5。
        /// </summary>
        /// <param name="Node">范围表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleRange(RangeExpressionSyntax Node)
        {
            var Left = Node.LeftOperand != null ? (ExpressionSyntax?)Visit(Node.LeftOperand) : null;
            var Right = Node.RightOperand != null ? (ExpressionSyntax?)Visit(Node.RightOperand) : null;

            return Node.Update(Left, Node.OperatorToken, Right);
        }

        /// <summary>
        /// 访问内插内容。
        /// </summary>
        /// <param name="Node">内插内容节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitInterpolation(InterpolationSyntax Node)
        {
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理内插字符串。
        /// 示例：
        /// $"a {b} c"，若移除内插部分 {b}，则变为 "a  c"。
        /// </summary>
        /// <param name="Node">内插字符串节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleInterpolatedString(InterpolatedStringExpressionSyntax Node)
        {
            var Contents = Node.Contents
                .Select(c => Visit(c))
                .Where(c => c != null)
                .Cast<InterpolatedStringContentSyntax>()
                .ToList();
            return Node.WithContents(SyntaxFactory.List(Contents));
        }

        /// <summary>
        /// 处理匿名对象创建。
        /// 遍历成员初始化器。若成员被移除，则尝试根据匿名类型属性生成占位符，以保持对象的匿名类型结构（Property 数量和顺序）。
        /// </summary>
        /// <param name="Node">匿名对象创建节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleAnonymousObjectCreation(AnonymousObjectCreationExpressionSyntax Node)
        {
            var Initializers = Node.Initializers
                .Select(i => (AnonymousObjectMemberDeclaratorSyntax?)Visit(i))
                .Where(i => i != null)
                .Cast<AnonymousObjectMemberDeclaratorSyntax>()
                .ToList();

            var OriginalNode = GetOriginalNode(Node) as AnonymousObjectCreationExpressionSyntax;
            if (Initializers.Count < Node.Initializers.Count && this.Model != null && OriginalNode != null)
            {
                var TypeInfo = this.Model.GetTypeInfo(OriginalNode);
                var AnonType = TypeInfo.ConvertedType as INamedTypeSymbol;
                var NewInitializers = new System.Collections.Generic.List<AnonymousObjectMemberDeclaratorSyntax>();
                for (int i = 0; i < Node.Initializers.Count; i++)
                {
                    var OriginalInit = Node.Initializers[i];
                    var ProcessedInit = (AnonymousObjectMemberDeclaratorSyntax?)Visit(OriginalInit);
                    if (ProcessedInit != null)
                    {
                        NewInitializers.Add(ProcessedInit);
                    }
                    else
                    {
                        ITypeSymbol? MemberType = null;
                        if (AnonType != null && i < AnonType.GetMembers().Length)
                        {
                            var Member = AnonType.GetMembers()[i];
                            if (Member is IPropertySymbol Prop) MemberType = Prop.Type;
                        }

                        if (MemberType == null)
                        {
                            var Placeholder = TryGetPlaceholderExpression(OriginalInit.Expression);
                            if (Placeholder != null)
                            {
                                NewInitializers.Add(OriginalInit.WithExpression(Placeholder));
                                continue;
                            }
                        }

                        if (MemberType != null)
                        {
                            NewInitializers.Add(OriginalInit.WithExpression(CreatePlaceholder(MemberType)));
                        }
                        else
                        {
                            NewInitializers.Add(OriginalInit.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
                        }
                    }
                }
                Initializers = NewInitializers;
            }

            return Node.WithInitializers(SyntaxFactory.SeparatedList(Initializers));
        }

        /// <summary>
        /// 访问匿名对象成员声明。
        /// </summary>
        /// <param name="Node">成员声明节点</param>
        /// <returns>简化后的节点</returns>
        public override SyntaxNode? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax Node)
        {
            var Expression = Visit(Node.Expression);
            if (Expression == null)
            {
                var Placeholder = TryGetPlaceholderExpression(Node.Expression);
                if (Placeholder != null) return Node.WithExpression(Placeholder);
            }
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理 checked/unchecked 表达式。
        /// 示例：
        /// checked(a + b)，若移除 a + b，则返回 null。
        /// </summary>
        /// <param name="Node">checked/unchecked 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleChecked(CheckedExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理 default 表达式。
        /// 示例：
        /// default(T)，若移除 T，则返回 null。
        /// </summary>
        /// <param name="Node">default 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleDefault(DefaultExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Type)) return null;
            var Type = Visit(Node.Type);
            if (Type == null) return null;
            return Node.WithType((TypeSyntax)Type);
        }

        /// <summary>
        /// 处理查询表达式 (LINQ)。
        /// 示例：
        /// from x in list select x，若移除 list，则返回 null。
        /// </summary>
        /// <param name="Node">查询表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleQuery(QueryExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.FromClause.Expression)) return null;
            var FromExpression = Visit(Node.FromClause.Expression);
            if (FromExpression == null) return null;

            var FromClause = (FromClauseSyntax?)Visit(Node.FromClause);
            var Body = (QueryBodySyntax?)Visit(Node.Body);

            if (FromClause == null || Body == null) return null;

            return Node.Update(FromClause, Body);
        }

        /// <summary>
        /// 处理 ref 表达式。
        /// 示例：
        /// ref a，若移除 a，则返回 null。
        /// </summary>
        /// <param name="Node">ref 表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleRef(RefExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理 __refvalue。
        /// 示例：
        /// __refvalue(typedRef, int)，若移除 typedRef，则返回 null。
        /// </summary>
        /// <param name="Node">__refvalue 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleRefValue(RefValueExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理 __makeref。
        /// 示例：
        /// __makeref(a)，若移除 a，则返回 null。
        /// </summary>
        /// <param name="Node">__makeref 节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleMakeRef(MakeRefExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Expression)) return null;
            var Expression = Visit(Node.Expression);
            if (Expression == null) return null;
            return Node.WithExpression((ExpressionSyntax)Expression);
        }

        /// <summary>
        /// 处理元素绑定。
        /// 示例：
        /// .Prop[0]，其中 [0] 是元素绑定，若移除 0，则返回 null。
        /// </summary>
        /// <param name="Node">元素绑定节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleElementBinding(ElementBindingExpressionSyntax Node)
        {
            var Args = Node.ArgumentList.Arguments
                .Select(a => (ArgumentSyntax?)Visit(a))
                .Where(a => a != null)
                .Cast<ArgumentSyntax>()
                .ToList();

            if (Args.Count == 0 && Node.ArgumentList.Arguments.Count > 0) return null;

            return Node.Update(Node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(Args)));
        }

        /// <summary>
        /// 处理成员绑定。
        /// 示例：
        /// .Prop，其中 Prop 是成员绑定，若移除 Prop，则返回 null。
        /// </summary>
        /// <param name="Node">成员绑定节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleMemberBinding(MemberBindingExpressionSyntax Node)
        {
            if (this.ShouldRemove(Node.Name)) return null;
            var Name = (SimpleNameSyntax?)Visit(Node.Name);
            if (Name == null) return null;
            return Node.WithName(Name);
        }

        /// <summary>
        /// 处理隐式元素访问。
        /// 示例：
        /// obj?[index]，若移除 index，则返回 null。
        /// </summary>
        /// <param name="Node">隐式元素访问节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleImplicitElementAccess(ImplicitElementAccessSyntax Node)
        {
            var Args = Node.ArgumentList.Arguments
                .Select(a => (ArgumentSyntax?)Visit(a))
                .Where(a => a != null)
                .Cast<ArgumentSyntax>()
                .ToList();

            if (Args.Count == 0 && Node.ArgumentList.Arguments.Count > 0) return null;

            return Node.Update(Node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(Args)));
        }

        /// <summary>
        /// 处理实例表达式 (this, base)。
        /// 示例：
        /// this 或 base，若标记为移除，则返回 null。
        /// </summary>
        /// <param name="Node">实例表达式节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleInstance(InstanceExpressionSyntax Node) => this.ShouldRemove(Node) ? null : base.Visit(Node);

        /// <summary>
        /// 处理省略的数组大小。
        /// 示例：
        /// new int[]，其中的空括号，若标记为移除，则返回 null。
        /// </summary>
        /// <param name="Node">省略的数组大小节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleOmittedArraySize(OmittedArraySizeExpressionSyntax Node) => this.ShouldRemove(Node) ? null : base.VisitOmittedArraySizeExpression(Node);

        /// <summary>
        /// 处理字面量表达式。
        /// 示例：
        /// 123, "str"，若标记为移除，则返回 null。
        /// </summary>
        /// <param name="Node">字面量节点</param>
        /// <returns>简化后的节点</returns>
        private SyntaxNode? HandleLiteral(LiteralExpressionSyntax Node) => this.ShouldRemove(Node) ? null : base.VisitLiteralExpression(Node);
    }
}
