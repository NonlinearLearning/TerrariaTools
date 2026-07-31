using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace NLISSN.Core.Analysis;

/// 表示目标符号在逻辑条件中的命中方式。
public enum LogicalConditionHitKind
{
    Direct = 0,
    UnaryWrapped = 1
}

/// 记录一次逻辑条件命中及其对应的包装语法。
public sealed record LogicalConditionHit(
    ISymbol TargetSymbol,
    SyntaxNode Node,
    LogicalConditionHitKind HitKind,
    ExpressionSyntax? UnderlyingExpression,
    PrefixUnaryExpressionSyntax? WrapperNode);

/// 汇总逻辑条件在标记阶段产出的分析事实。
public sealed record LogicalConditionMarkAnalysis(
    MarkCodeRegion Region,
    ISymbol TargetSymbol,
    IReadOnlyList<ISymbol> TargetSymbols,
    IReadOnlyList<LogicalConditionHit> Hits,
    IReadOnlyList<BinaryExpressionSyntax> OperandGroups,
    SyntaxNode PreferredMarkedNode);

/// 负责在单个逻辑条件里解析目标命中与操作数组。
public sealed class LogicalConditionMarkAnalyzer
{
    // 在逻辑条件内部定位目标命中、操作数组和优选标记节点，供 mark 阶段直接使用。
    public LogicalConditionMarkAnalysis Analyze(
      ExpressionSyntax seedExpression,
      string targetName,
      CpgAnalysisContext context,
      MarkAnalysisSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Analyze(
          seedExpression,
          targetName,
          context,
          snapshot.GetOperation);
    }

    internal LogicalConditionMarkAnalysis Analyze(
      ExpressionSyntax seedExpression,
      string targetName,
      CpgAnalysisContext context,
      Func<ExpressionSyntax, IOperation?> resolveOperation)
    {
        ArgumentNullException.ThrowIfNull(resolveOperation);
        var region = new MarkRegionAnalyzer().Analyze(seedExpression, context);
        var conditionRoot = FindConditionRoot(seedExpression, region)
            ?? throw new InvalidOperationException(
                $"Expression '{seedExpression}' is not inside a supported logical condition.");
        var targetNames = ParseTargetNames(targetName);
        var targetSymbols = ResolveTargetSymbols(
          conditionRoot,
          targetNames,
          context.SemanticModel,
          resolveOperation);
        if (targetSymbols.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not resolve target symbol '{targetName}' from expression '{seedExpression}'.");
        }

        var hits = CollectHits(conditionRoot, region, targetSymbols, context, resolveOperation);
        if (hits.Count == 0)
        {
            throw new InvalidOperationException(
                $"Logical condition analysis found no hits for target '{targetName}'.");
        }

        var operandGroups = CollectOperandGroups(conditionRoot, context);
        var preferredMarkedNode = ResolvePreferredMarkedNode(conditionRoot, hits, context);
        return new LogicalConditionMarkAnalysis(
            region,
            targetSymbols[0],
            targetSymbols,
            hits,
            operandGroups,
            preferredMarkedNode);
    }

    // 快速判断一个表达式是否落在当前支持的逻辑条件分析范围内。
    public bool CanAnalyze(ExpressionSyntax expression, CpgAnalysisContext context)
    {
        var region = new MarkRegionAnalyzer().Analyze(expression, context);
        return FindConditionRoot(expression, region) is not null;
    }

    private static IReadOnlyList<LogicalConditionHit> CollectHits(
      ExpressionSyntax conditionRoot,
      MarkCodeRegion region,
      IReadOnlyList<ISymbol> targetSymbols,
      CpgAnalysisContext context,
      Func<ExpressionSyntax, IOperation?> resolveOperation)
    {
        var hits = new List<LogicalConditionHit>();

        var atomicExpressionAnalyzer = new AtomicExpressionAnalyzer();
        // 单独保留 !target 的命中，后续阶段才能区分直接命中和逻辑非包装命中。
        foreach (var prefixUnary in conditionRoot.DescendantNodesAndSelf().OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) ||
                prefixUnary.Operand is not ExpressionSyntax operand ||
                !region.Span.Contains(prefixUnary.Span))
            {
                continue;
            }

            var unaryAnalysis = new UnaryExpressionAnalyzer().Analyze(prefixUnary, context);
            if (!unaryAnalysis.AffectedSyntaxTree.Contains(operand))
            {
                continue;
            }

            var matchedTargetSymbol = ResolveMatchedTargetSymbol(
                operand,
                targetSymbols,
                context.SemanticModel,
                resolveOperation);
            if (matchedTargetSymbol is not null)
            {
                hits.Add(new LogicalConditionHit(
                    matchedTargetSymbol,
                    prefixUnary,
                    LogicalConditionHitKind.UnaryWrapped,
                    operand,
                    prefixUnary));
            }
        }

        // 只有原子表达式能直接成为种子标记，更大的逻辑宿主留给后续阶段回推。
        foreach (var expression in atomicExpressionAnalyzer.Analyze(conditionRoot))
        {
            if (!region.Span.Contains(expression.Span) ||
                expression.Parent is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                continue;
            }

            var matchedTargetSymbol = ResolveMatchedTargetSymbol(
                expression,
                targetSymbols,
                context.SemanticModel,
                resolveOperation);
            if (matchedTargetSymbol is not null)
            {
                hits.Add(new LogicalConditionHit(
                    matchedTargetSymbol,
                    expression,
                    LogicalConditionHitKind.Direct,
                    expression,
                    null));
            }
        }

        return hits
            .DistinctBy(hit => (hit.Node.SpanStart, hit.Node.Span.Length, hit.HitKind))
            .OrderBy(hit => hit.Node.SpanStart)
            .ThenBy(hit => hit.Node.Span.Length)
            .ToList();
    }

    private static IReadOnlyList<BinaryExpressionSyntax> CollectOperandGroups(ExpressionSyntax conditionRoot, CpgAnalysisContext context)
    {
        var groups = new List<BinaryExpressionSyntax>();

        foreach (var binaryExpression in conditionRoot.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>())
        {
            if (!binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                !binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
            {
                continue;
            }

            _ = new BinaryExpressionAnalyzer().Analyze(binaryExpression, binaryExpression.Left, context);
            groups.Add(binaryExpression);
        }

        return groups
            .Distinct()
            .OrderBy(node => node.SpanStart)
            .ThenByDescending(node => node.Span.Length)
            .ToList();
    }

    private static SyntaxNode ResolvePreferredMarkedNode(ExpressionSyntax conditionRoot, IReadOnlyList<LogicalConditionHit> hits, CpgAnalysisContext context)
    {
        var logicalAncestorsByHit = hits
            .Select(hit => CollectLogicalAncestors(hit.Node, context))
            .ToList();
        var commonAncestors = logicalAncestorsByHit.Count == 0
            ? new List<BinaryExpressionSyntax>()
            : logicalAncestorsByHit
                .Skip(1)
                .Aggregate(
                    logicalAncestorsByHit[0].ToHashSet(),
                    (current, next) =>
                    {
                        current.IntersectWith(next);
                        return current;
                    })
                .OrderBy(node => node.SpanStart)
                .ThenByDescending(node => node.Span.Length)
                .ToList();

        if (commonAncestors.Count > 0)
        {
            return commonAncestors
                .OrderByDescending(node => node.Span.Length)
                .ThenBy(node => node.SpanStart)
                .First();
        }

        if (conditionRoot is BinaryExpressionSyntax binaryExpression &&
            (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
             binaryExpression.IsKind(SyntaxKind.LogicalOrExpression)))
        {
            _ = new BinaryExpressionAnalyzer().Analyze(binaryExpression, binaryExpression.Left, context);
            return binaryExpression;
        }

        return conditionRoot;
    }

    private static List<BinaryExpressionSyntax> CollectLogicalAncestors(SyntaxNode node, CpgAnalysisContext context)
    {
        var logicalAncestors = new List<BinaryExpressionSyntax>();

        for (var current = node as ExpressionSyntax ?? node.Parent as ExpressionSyntax;
             current is not null;
             current = current.Parent as ExpressionSyntax)
        {
            if (current is not BinaryExpressionSyntax binaryExpression)
            {
                continue;
            }

            if (!binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                !binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
            {
                continue;
            }

            logicalAncestors.Add(binaryExpression);
        }

        return logicalAncestors;
    }

    private static ExpressionSyntax? FindConditionRoot(ExpressionSyntax expression, MarkCodeRegion region)
    {
        foreach (var current in expression.AncestorsAndSelf().OfType<ExpressionSyntax>())
        {
            if (!region.Span.Contains(current.Span))
            {
                break;
            }

            if (current.Parent is IfStatementSyntax ifStatement &&
                ReferenceEquals(ifStatement.Condition, current))
            {
                return UnwrapParenthesizedExpression(current);
            }

            if (current.Parent is WhileStatementSyntax whileStatement &&
                ReferenceEquals(whileStatement.Condition, current))
            {
                return UnwrapParenthesizedExpression(current);
            }

            if (current.Parent is DoStatementSyntax doStatement &&
                ReferenceEquals(doStatement.Condition, current))
            {
                return UnwrapParenthesizedExpression(current);
            }

            if (current.Parent is ForStatementSyntax forStatement &&
                ReferenceEquals(forStatement.Condition, current))
            {
                return UnwrapParenthesizedExpression(current);
            }

            if (current.Parent is ConditionalExpressionSyntax conditionalExpression &&
                ReferenceEquals(conditionalExpression.Condition, current))
            {
                return UnwrapParenthesizedExpression(current);
            }
        }

        return expression.AncestorsAndSelf()
            .OfType<BinaryExpressionSyntax>()
            .Where(node =>
                region.Span.Contains(node.Span) &&
                (node.IsKind(SyntaxKind.LogicalAndExpression) ||
                 node.IsKind(SyntaxKind.LogicalOrExpression)))
            .OrderByDescending(node => node.Span.Length)
            .FirstOrDefault();
    }

    private static ExpressionSyntax UnwrapParenthesizedExpression(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            current = parenthesizedExpression.Expression;
        }

        return current;
    }

    private static IReadOnlyList<string> ParseTargetNames(string targetName)
    {
        return targetName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<ISymbol> ResolveTargetSymbols(
      ExpressionSyntax expression,
      IReadOnlyList<string> targetNames,
      SemanticModel semanticModel,
      Func<ExpressionSyntax, IOperation?> resolveOperation)
    {
        return expression.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Select(node => ResolveSymbol(node, semanticModel, resolveOperation))
            .Where(symbol => symbol is not null && targetNames.Contains(symbol.Name, StringComparer.Ordinal))
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<ISymbol>()
            .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static ISymbol? ResolveMatchedTargetSymbol(ISymbol? candidateSymbol, IReadOnlyList<ISymbol> targetSymbols)
    {
        if (candidateSymbol is null)
        {
            return null;
        }

        foreach (var targetSymbol in targetSymbols)
        {
            if (SymbolEqualityComparer.Default.Equals(candidateSymbol, targetSymbol))
            {
                return targetSymbol;
            }
        }

        return null;
    }

    private static ISymbol? ResolveMatchedTargetSymbol(
      ExpressionSyntax expression,
      IReadOnlyList<ISymbol> targetSymbols,
      SemanticModel semanticModel,
      Func<ExpressionSyntax, IOperation?> resolveOperation)
    {
        var operation = resolveOperation(expression);
        if (operation is null)
        {
            var symbol = ResolveSymbol(expression, semanticModel, resolveOperation);
            return ResolveMatchedTargetSymbol(symbol, targetSymbols);
        }

        return ResolveMatchedTargetSymbol(operation, targetSymbols);
    }

    private static ISymbol? ResolveMatchedTargetSymbol(IOperation operation, IReadOnlyList<ISymbol> targetSymbols)
    {
        var symbol = ResolveOperationSymbol(operation);
        var matchedTargetSymbol = ResolveMatchedTargetSymbol(symbol, targetSymbols);
        if (matchedTargetSymbol is not null)
        {
            return matchedTargetSymbol;
        }

        foreach (var child in operation.ChildOperations)
        {
            matchedTargetSymbol = ResolveMatchedTargetSymbol(child, targetSymbols);
            if (matchedTargetSymbol is not null)
            {
                return matchedTargetSymbol;
            }
        }

        return null;
    }

    private static ISymbol? ResolveSymbol(
      ExpressionSyntax expression,
      SemanticModel semanticModel,
      Func<ExpressionSyntax, IOperation?> resolveOperation)
    {
        var operation = resolveOperation(expression);
        if (operation is not null)
        {
            return ResolveOperationSymbol(operation);
        }

        return semanticModel.GetSymbolInfo(expression).Symbol;
    }

    private static ISymbol? ResolveOperationSymbol(IOperation operation)
    {
        return operation switch
        {
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            IInvocationOperation invocation => invocation.TargetMethod,
            _ => null
        };
    }

}
