using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System;

namespace TerrariaTools.RewriteCodeExpressions.Pipeline
{
    /// <summary>
    /// 占位符工厂：负责为被移除的语法节点生成符合语义的替代物。
    /// </summary>
    public static class PlaceholderFactory
    {
        /// <summary>
        /// 判断是否可以为指定节点创建占位符。
        /// </summary>
        /// <param name="node">语法节点。</param>
        /// <param name="model">可选的语义模型。</param>
        /// <returns>如果可以创建占位符则返回 true，否则返回 false。</returns>
        public static bool CanCreatePlaceholder(SyntaxNode node, SemanticModel? model)
        {
            if (node == null) return false;

            if (node is ArgumentSyntax || node is ParameterSyntax || node is SingleVariableDesignationSyntax ||
                node is SwitchExpressionArmSyntax || node is ArrowExpressionClauseSyntax)
                return true;

            if (node is ExpressionSyntax expr)
            {
                if (model == null) return true; // 默认允许
                var typeInfo = model.GetTypeInfo(expr);
                var type = typeInfo.ConvertedType ?? typeInfo.Type;
                return type != null && type.TypeKind != TypeKind.Error;
            }

            if (node is StatementSyntax stmt)
            {
                return stmt.Parent is IfStatementSyntax || stmt.Parent is ElseClauseSyntax ||
                       stmt.Parent is ForStatementSyntax || stmt.Parent is WhileStatementSyntax ||
                       stmt.Parent is DoStatementSyntax || (stmt is YieldStatementSyntax);
            }

            return false;
        }

        /// <summary>
        /// 尝试为指定节点创建占位符。
        /// </summary>
        /// <param name="node">语法节点。</param>
        /// <param name="model">可选的语义模型。</param>
        /// <param name="generator">语法生成器。</param>
        /// <returns>创建的占位符语法节点，如果无法创建则返回 null。</returns>
        public static SyntaxNode? CreatePlaceholder(SyntaxNode node, SemanticModel? model, SyntaxGenerator generator)
        {
            if (node == null) return null;

            switch (node)
            {
                case ArgumentSyntax arg:
                    return CreateArgumentPlaceholder(arg, model, generator);

                case ExpressionSyntax expr:
                    return CreateExpressionPlaceholder(expr, model, generator);

                case StatementSyntax stmt:
                    return CreateStatementPlaceholder(stmt, model, generator);

                case ParameterSyntax param:
                    return generator.ParameterDeclaration(param.Identifier.Text, generator.TypeExpression(SpecialType.System_Object));

                case SingleVariableDesignationSyntax:
                    return SyntaxFactory.DiscardDesignation();

                case SwitchExpressionArmSyntax arm:
                    return CreateSwitchExpressionArmPlaceholder(arm, model, generator);

                case ArrowExpressionClauseSyntax arrow:
                    return CreateArrowExpressionPlaceholder(arrow, model, generator);

                case AnonymousObjectMemberDeclaratorSyntax anonMem:
                    return CreateAnonymousObjectMemberPlaceholder(anonMem, model, generator);
            }

            return null;
        }

        /// <summary>
        /// 为匿名对象成员声明器创建占位符。
        /// </summary>
        private static SyntaxNode? CreateAnonymousObjectMemberPlaceholder(AnonymousObjectMemberDeclaratorSyntax anonMem, SemanticModel? model, SyntaxGenerator generator)
        {
            var exprPlaceholder = CreateExpressionPlaceholder(anonMem.Expression, model, generator);
            if (exprPlaceholder is ExpressionSyntax expr)
            {
                return anonMem.WithExpression(expr);
            }
            return null;
        }

        /// <summary>
        /// 为 Switch 表达式分支创建占位符。
        /// </summary>
        private static SyntaxNode? CreateSwitchExpressionArmPlaceholder(SwitchExpressionArmSyntax arm, SemanticModel? model, SyntaxGenerator generator)
        {
            if (model == null) return null;

            // 获取 Switch 表达式的类型
            var switchExpr = arm.Parent as SwitchExpressionSyntax;
            if (switchExpr != null)
            {
                var typeInfo = model.GetTypeInfo(switchExpr);
                var type = typeInfo.ConvertedType ?? typeInfo.Type;
                if (type != null)
                {
                    var placeholder = CreatePlaceholderForType(type, generator);
                    return arm.WithExpression(placeholder);
                }
            }
            return null;
        }

        /// <summary>
        /// 为箭头表达式子句创建占位符。
        /// </summary>
        private static SyntaxNode? CreateArrowExpressionPlaceholder(ArrowExpressionClauseSyntax arrow, SemanticModel? model, SyntaxGenerator generator)
        {
            if (model == null) return null;

            var symbol = model.GetEnclosingSymbol(arrow.SpanStart);
            ITypeSymbol? returnType = null;

            if (symbol is IMethodSymbol method) returnType = method.ReturnType;
            else if (symbol is IPropertySymbol property) returnType = property.Type;

            if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
            {
                var placeholder = CreatePlaceholderForType(returnType, generator);
                return arrow.WithExpression(placeholder);
            }
            return null;
        }

        /// <summary>
        /// 为方法或索引器参数创建占位符。
        /// </summary>
        private static SyntaxNode? CreateArgumentPlaceholder(ArgumentSyntax arg, SemanticModel? model, SyntaxGenerator generator)
        {
            if (model == null) return generator.LiteralExpression(null);

            var argList = arg.Parent;
            var callNode = argList?.Parent;
            if (callNode != null && model != null)
            {
                var symbolInfo = model.GetSymbolInfo(callNode);
                var method = symbolInfo.Symbol as IMethodSymbol;

                if (method == null && callNode is ElementAccessExpressionSyntax)
                {
                    var property = symbolInfo.Symbol as IPropertySymbol;
                    if (property != null) method = property.Parameters.Length > 0 ? property.GetMethod : null;
                }

                if (method != null)
                {
                    int index = -1;
                    if (argList is ArgumentListSyntax al) index = al.Arguments.IndexOf(arg);
                    else if (argList is BracketedArgumentListSyntax bal) index = bal.Arguments.IndexOf(arg);

                    if (index >= 0 && index < method.Parameters.Length)
                    {
                        var paramType = method.Parameters[index].Type;
                        return arg.WithExpression(CreatePlaceholderForType(paramType, generator));
                    }
                }

                // 数组索引处理
                if (callNode is ElementAccessExpressionSyntax eae && model.GetTypeInfo(eae.Expression).Type is IArrayTypeSymbol)
                {
                    return arg.WithExpression((ExpressionSyntax)generator.LiteralExpression(0));
                }
            }

            var exprPlaceholder = CreateExpressionPlaceholder(arg.Expression, model, generator);
            if (exprPlaceholder is ExpressionSyntax expr)
            {
                return arg.WithExpression(expr);
            }
            return null;
        }

        /// <summary>
        /// 为表达式创建占位符。
        /// </summary>
        private static SyntaxNode? CreateExpressionPlaceholder(ExpressionSyntax expr, SemanticModel? model, SyntaxGenerator generator)
        {
            if (model == null) return generator.LiteralExpression(null);

            // 特殊处理一元负号
            if (expr is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                var operandPlaceholder = CreateExpressionPlaceholder(unary.Operand, model, generator);
                if (operandPlaceholder is ExpressionSyntax op)
                {
                    return unary.WithOperand(op);
                }
            }

            var typeInfo = model.GetTypeInfo(expr);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;

            if (type == null || type.TypeKind == TypeKind.Error)
            {
                // 启发式兜底逻辑
                if (expr is LiteralExpressionSyntax literal)
                {
                    if (literal.IsKind(SyntaxKind.NumericLiteralExpression)) return generator.LiteralExpression(0);
                    if (literal.IsKind(SyntaxKind.StringLiteralExpression)) return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                        SyntaxFactory.IdentifierName("Empty"));
                    if (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression)) return generator.LiteralExpression(false);
                }
                return generator.LiteralExpression(null);
            }

            return CreatePlaceholderForType(type, generator);
        }

        /// <summary>
        /// 为指定的类型符号创建默认值占位符。
        /// </summary>
        /// <param name="Type">类型符号。</param>
        /// <param name="generator">语法生成器。</param>
        /// <returns>代表默认值的表达式节点。</returns>
        public static ExpressionSyntax? CreatePlaceholderForType(ITypeSymbol Type, SyntaxGenerator generator)
        {
            if (Type == null) return null;

            // 1. 处理常见特殊类型
            switch (Type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return (ExpressionSyntax)generator.LiteralExpression(false);
                case SpecialType.System_String:
                    return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                        SyntaxFactory.IdentifierName("Empty"));
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                    return (ExpressionSyntax)generator.LiteralExpression(0);
                case SpecialType.System_Object:
                    return (ExpressionSyntax)generator.LiteralExpression(null);
            }

            // 2. 处理异步 Task 类型
            var FullName = Type.ToDisplayString();
            if (FullName == "System.Threading.Tasks.Task" || FullName == "System.Threading.Tasks.ValueTask")
            {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseTypeName("System.Threading.Tasks.Task"),
                    SyntaxFactory.IdentifierName("CompletedTask"));
            }
            if (Type is INamedTypeSymbol NamedType && NamedType.IsGenericType &&
                (FullName.StartsWith("System.Threading.Tasks.Task<") || FullName.StartsWith("System.Threading.Tasks.ValueTask<")))
            {
                var T = NamedType.TypeArguments[0];
                var typeT = T.ToDisplayString();
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName("System.Threading.Tasks.Task"),
                        SyntaxFactory.GenericName(
                            SyntaxFactory.Identifier("FromResult"),
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.ParseTypeName(typeT))))),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(CreatePlaceholderForType(T, generator)))));
            }

            // 3. 处理数组和常见泛型集合
            if (Type.TypeKind == TypeKind.Array)
            {
                var ArrayType = (IArrayTypeSymbol)Type;
                return SyntaxFactory.ArrayCreationExpression(
                    SyntaxFactory.ArrayType(SyntaxFactory.ParseTypeName(ArrayType.ElementType.ToDisplayString()))
                        .WithRankSpecifiers(SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                            (ExpressionSyntax)generator.LiteralExpression(0))))));
            }

            if (Type is INamedTypeSymbol CollectionType && CollectionType.IsGenericType)
            {
                var BaseName = CollectionType.ConstructedFrom.ToDisplayString();
                if (BaseName == "System.Collections.Generic.IEnumerable<T>" ||
                    BaseName == "System.Collections.Generic.IList<T>" ||
                    BaseName == "System.Collections.Generic.IReadOnlyList<T>" ||
                    BaseName == "System.Collections.Generic.ICollection<T>")
                {
                    var typeT = CollectionType.TypeArguments[0].ToDisplayString();
                    return SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName("System.Linq.Enumerable"),
                            SyntaxFactory.GenericName(
                                SyntaxFactory.Identifier("Empty"),
                                SyntaxFactory.TypeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.ParseTypeName(typeT))))));
                }
                if (BaseName == "System.Collections.Generic.List<T>")
                {
                    var typeT = CollectionType.TypeArguments[0].ToDisplayString();
                    return SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.ParseTypeName("System.Collections.Generic.List<" + typeT + ">"),
                        SyntaxFactory.ArgumentList(),
                        null);
                }
            }

            // 4. 处理元组和泛型参数
            if (Type.IsTupleType || Type.TypeKind == TypeKind.TypeParameter)
            {
                return SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(Type.ToDisplayString()));
            }

            return (ExpressionSyntax)generator.DefaultExpression(Type);
        }

        /// <summary>
        /// 为语句创建占位符。
        /// </summary>
        private static SyntaxNode? CreateStatementPlaceholder(StatementSyntax stmt, SemanticModel? model, SyntaxGenerator generator)
        {
            // 确保移除语句后不会破坏原本的控制流结构（如 if/else/for/while）
            if (stmt.Parent is IfStatementSyntax || stmt.Parent is ElseClauseSyntax || stmt.Parent is ForStatementSyntax || stmt.Parent is WhileStatementSyntax || stmt.Parent is DoStatementSyntax)
            {
                return SyntaxFactory.EmptyStatement();
            }

            if (stmt is ReturnStatementSyntax returnStmt)
            {
                if (model != null)
                {
                    var parent = returnStmt.Parent;
                    while (parent != null)
                    {
                        if (parent is MethodDeclarationSyntax m)
                        {
                            var methodSymbol = model.GetDeclaredSymbol(m);
                            if (methodSymbol != null && !methodSymbol.ReturnsVoid)
                            {
                                var expr = CreatePlaceholderForType(methodSymbol.ReturnType, generator);
                                if (expr != null) return returnStmt.WithExpression((ExpressionSyntax)expr);
                            }
                            break;
                        }
                        if (parent is LocalFunctionStatementSyntax l)
                        {
                            var methodSymbol = model.GetDeclaredSymbol(l);
                            if (methodSymbol != null && !methodSymbol.ReturnsVoid)
                            {
                                var expr = CreatePlaceholderForType(methodSymbol.ReturnType, generator);
                                if (expr != null) return returnStmt.WithExpression((ExpressionSyntax)expr);
                            }
                            break;
                        }
                        if (parent is AccessorDeclarationSyntax a)
                        {
                            var methodSymbol = model.GetDeclaredSymbol(a);
                            if (methodSymbol != null && !methodSymbol.ReturnsVoid)
                            {
                                var expr = CreatePlaceholderForType(methodSymbol.ReturnType, generator);
                                if (expr != null) return returnStmt.WithExpression((ExpressionSyntax)expr);
                            }
                            break;
                        }
                        if (parent is AnonymousFunctionExpressionSyntax f)
                        {
                            var symbolInfo = model.GetSymbolInfo(f);
                            if (symbolInfo.Symbol is IMethodSymbol ms && !ms.ReturnsVoid)
                            {
                                var expr = CreatePlaceholderForType(ms.ReturnType, generator);
                                if (expr != null) return returnStmt.WithExpression((ExpressionSyntax)expr);
                            }
                            break;
                        }
                        parent = parent.Parent;
                    }
                }
                // 兜底逻辑：无法确定类型时
                if (returnStmt.Expression != null)
                {
                    return returnStmt.WithExpression((ExpressionSyntax)generator.LiteralExpression(null));
                }
                return returnStmt;
            }

            if (stmt is YieldStatementSyntax yield && yield.ReturnOrBreakKeyword.IsKind(SyntaxKind.ReturnKeyword))
            {
                if (model != null)
                {
                    var symbol = model.GetEnclosingSymbol(yield.SpanStart);
                    if (symbol is IMethodSymbol method && method.ReturnType is INamedTypeSymbol named && named.IsGenericType)
                    {
                        var returnType = named.TypeArguments.FirstOrDefault();
                        if (returnType != null)
                        {
                            return yield.WithExpression(CreatePlaceholderForType(returnType, generator));
                        }
                    }
                }
                return yield.WithExpression((ExpressionSyntax)generator.LiteralExpression(null));
            }

            return null;
        }

        /// <summary>
        /// 判断节点所在上下文是否必须要求一个值。
        /// </summary>
        /// <param name="node">语法节点。</param>
        /// <returns>如果上下文必须要求值则返回 true，否则返回 false。</returns>
        public static bool IsValueRequiredContext(SyntaxNode node)
        {
            var parent = node.Parent;
            if (parent == null) return false;

            return parent switch
            {
                BlockSyntax => node is YieldStatementSyntax yield && yield.ReturnOrBreakKeyword.IsKind(SyntaxKind.ReturnKeyword),
                ReturnStatementSyntax => true,
                ArrowExpressionClauseSyntax => true,
                EqualsValueClauseSyntax => true,
                IfStatementSyntax ifStmt => ifStmt.Condition == node,
                WhileStatementSyntax whileStmt => whileStmt.Condition == node,
                DoStatementSyntax doStmt => doStmt.Condition == node,
                ForStatementSyntax forStmt => forStmt.Condition == node,
                SwitchStatementSyntax switchStmt => switchStmt.Expression == node,
                SwitchExpressionSyntax switchExpr => switchExpr.GoverningExpression == node,
                SwitchExpressionArmSyntax arm => arm.Expression == node,
                ConditionalExpressionSyntax => true,
                ArgumentSyntax => true,
                AttributeArgumentSyntax => true,
                BracketedArgumentListSyntax => true,
                ArgumentListSyntax => true,
                AttributeArgumentListSyntax => true,
                AnonymousObjectMemberDeclaratorSyntax anonMem => true,
                AssignmentExpressionSyntax assign => assign.Right == node,
                InitializerExpressionSyntax => true,
                AnonymousObjectCreationExpressionSyntax => true,
                PrefixUnaryExpressionSyntax => true,
                PostfixUnaryExpressionSyntax => true,
                BinaryExpressionSyntax => true,
                InterpolationSyntax => true,
                CastExpressionSyntax cast => cast.Expression == node,
                ElementAccessExpressionSyntax => true,
                InvocationExpressionSyntax => !(parent is ExpressionStatementSyntax),
                YieldStatementSyntax yieldStmt => yieldStmt.Expression == node || (yieldStmt.ReturnOrBreakKeyword.IsKind(SyntaxKind.ReturnKeyword) && node == yieldStmt),
                VariableDeclarationSyntax => false,
                _ => !(parent is StatementSyntax)
            };
        }
    }
}
