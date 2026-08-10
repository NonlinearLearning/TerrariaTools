using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace NLISSN.Core.Analysis.ExpressionPropagation;

public interface IExpressionPropagationTopology
{
    ExpressionTopologyPath Analyze(ExpressionSyntax seed);
}

public enum ExpressionTopologyKind
{
    Grouping,
    Wrapper,
    Binary,
    Unary,
    Conditional,
    Stop
}

public enum ExpressionPropagationMode
{
    Transparent,
    SiblingAndContinue,
    AggregateOnly,
    Terminal,
    TerminalMutation,
    TerminalWhole,
    Stop
}

public enum ExpressionTopologyTermination
{
    Root,
    Terminal,
    UnsupportedParent
}

public sealed record ExpressionOperatorFacts(
    SyntaxKind SyntaxKind,
    OperationKind? OperationKind,
    BinaryOperatorKind? BinaryOperatorKind,
    UnaryOperatorKind? UnaryOperatorKind,
    bool IsChecked,
    bool IsLifted,
    bool HasOperatorMethod)
{
    public static ExpressionOperatorFacts From(ExpressionSyntax expression, IOperation? operation)
    {
        return operation switch
        {
            IBinaryOperation binary => new ExpressionOperatorFacts(
                expression.Kind(), binary.Kind, binary.OperatorKind, null, binary.IsChecked,
                binary.IsLifted, binary.OperatorMethod is not null),
            IUnaryOperation unary => new ExpressionOperatorFacts(
                expression.Kind(), unary.Kind, null, unary.OperatorKind, unary.IsChecked,
                unary.IsLifted, unary.OperatorMethod is not null),
            IIncrementOrDecrementOperation increment => new ExpressionOperatorFacts(
                expression.Kind(), increment.Kind, null, null, increment.IsChecked,
                increment.IsLifted, increment.OperatorMethod is not null),
            _ => new ExpressionOperatorFacts(expression.Kind(), operation?.Kind, null, null, false, false, false)
        };
    }
}

public sealed record ExpressionTopologyStep(
    ExpressionSyntax Input,
    ExpressionSyntax Host,
    IReadOnlyList<ExpressionSyntax> DirectOperands,
    ExpressionTopologyKind Kind,
    ExpressionPropagationMode Mode,
    ExpressionOperatorFacts OperatorFacts)
{
    public bool CanContinueOutward => Mode is ExpressionPropagationMode.Transparent or
        ExpressionPropagationMode.SiblingAndContinue or ExpressionPropagationMode.AggregateOnly;
}

public sealed record ExpressionTopologyPath(
    ExpressionSyntax Seed,
    IReadOnlyList<ExpressionTopologyStep> Steps,
    ExpressionTopologyTermination Termination,
    IReadOnlyList<SyntaxNode> StructuralOwners);

/// <summary>Uses Roslyn's parsed direct-child relationships as expression topology.</summary>
public sealed class ExpressionPropagationTopology : IExpressionPropagationTopology
{
    private readonly Func<SyntaxNode, IOperation?> _getOperation;
    private readonly bool _requireSemanticOverlay;

    public ExpressionPropagationTopology(
        Func<SyntaxNode, IOperation?> getOperation,
        bool requireSemanticOverlay = false)
    {
        _getOperation = getOperation ?? throw new ArgumentNullException(nameof(getOperation));
        _requireSemanticOverlay = requireSemanticOverlay;
    }

    public ExpressionTopologyPath Analyze(ExpressionSyntax seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var steps = new List<ExpressionTopologyStep>();
        var current = seed;
        while (current.Parent is not null)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized when ReferenceEquals(parenthesized.Expression, current):
                    steps.Add(CreateStep(current, parenthesized, new[] { current }, ExpressionTopologyKind.Grouping, ExpressionPropagationMode.Transparent));
                    current = parenthesized;
                    continue;
                case CastExpressionSyntax cast when ReferenceEquals(cast.Expression, current):
                    steps.Add(CreateStep(current, cast, new[] { current }, ExpressionTopologyKind.Wrapper, ExpressionPropagationMode.Transparent));
                    current = cast;
                    continue;
                case CheckedExpressionSyntax checkedExpression when ReferenceEquals(checkedExpression.Expression, current):
                    steps.Add(CreateStep(current, checkedExpression, new[] { current }, ExpressionTopologyKind.Wrapper, ExpressionPropagationMode.Transparent));
                    current = checkedExpression;
                    continue;
                case AssignmentExpressionSyntax assignment when IsDirectOperand(assignment, current):
                    steps.Add(CreateStep(current, assignment, new[] { assignment.Left, assignment.Right }, ExpressionTopologyKind.Binary, ExpressionPropagationMode.Stop));
                    return new ExpressionTopologyPath(
                        seed,
                        steps,
                        ExpressionTopologyTermination.Terminal,
                        Array.Empty<SyntaxNode>());
                case BinaryExpressionSyntax binary when IsDirectOperand(binary, current):
                    var mode = GetBinaryMode(binary);
                    var step = CreateStep(
                        current,
                        binary,
                        new[] { binary.Left, binary.Right },
                        ExpressionTopologyKind.Binary,
                        mode);
                    steps.Add(step);
                    if (step.Mode == ExpressionPropagationMode.Stop)
                    {
                        return new ExpressionTopologyPath(
                            seed,
                            steps,
                            ExpressionTopologyTermination.Terminal,
                            BuildStructuralOwners(binary, includeStart: true));
                    }

                    current = binary;
                    continue;
                case PrefixUnaryExpressionSyntax prefix when ReferenceEquals(prefix.Operand, current):
                    return Terminal(
                        seed,
                        steps,
                        current,
                        prefix,
                        prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                        prefix.IsKind(SyntaxKind.PreDecrementExpression)
                          ? ExpressionPropagationMode.TerminalMutation
                          : ExpressionPropagationMode.Terminal);
                case PostfixUnaryExpressionSyntax postfix when ReferenceEquals(postfix.Operand, current):
                    return Terminal(seed, steps, current, postfix, ExpressionPropagationMode.TerminalMutation);
                case AwaitExpressionSyntax awaitExpression when ReferenceEquals(awaitExpression.Expression, current):
                    return Terminal(seed, steps, current, awaitExpression, ExpressionPropagationMode.Terminal);
                case ConditionalExpressionSyntax conditional when IsDirectOperand(conditional, current):
                    return Terminal(seed, steps, current, conditional, ExpressionPropagationMode.TerminalWhole);
                default:
                    return new ExpressionTopologyPath(
                        seed,
                        steps,
                        ExpressionTopologyTermination.UnsupportedParent,
                        CanExposeStructuralOwners(steps)
                          ? BuildStructuralOwners(current, includeStart: false)
                          : Array.Empty<SyntaxNode>());
            }
        }

        return new ExpressionTopologyPath(
            seed,
            steps,
            ExpressionTopologyTermination.Root,
            Array.Empty<SyntaxNode>());
    }

    private ExpressionTopologyPath Terminal(
        ExpressionSyntax seed,
        List<ExpressionTopologyStep> steps,
        ExpressionSyntax input,
        ExpressionSyntax host,
        ExpressionPropagationMode mode)
    {
        steps.Add(CreateStep(input, host, new[] { input }, host is ConditionalExpressionSyntax ? ExpressionTopologyKind.Conditional : ExpressionTopologyKind.Unary, mode));
        return new ExpressionTopologyPath(
            seed,
            steps,
            ExpressionTopologyTermination.Terminal,
            Array.Empty<SyntaxNode>());
    }

    private ExpressionTopologyStep CreateStep(
        ExpressionSyntax input,
        ExpressionSyntax host,
        IReadOnlyList<ExpressionSyntax> operands,
        ExpressionTopologyKind kind,
        ExpressionPropagationMode mode)
    {
        var operation = _getOperation(host);
        var facts = ExpressionOperatorFacts.From(host, operation);
        var effectiveMode = _requireSemanticOverlay && host is BinaryExpressionSyntax &&
          (operation is null || facts.HasOperatorMethod || facts.IsChecked || facts.IsLifted)
            ? ExpressionPropagationMode.Stop
            : mode;
        return new ExpressionTopologyStep(input, host, operands, kind, effectiveMode, facts);
    }

    private static ExpressionPropagationMode GetBinaryMode(BinaryExpressionSyntax binary)
    {
        return binary.Kind() switch
        {
            SyntaxKind.LogicalAndExpression => ExpressionPropagationMode.SiblingAndContinue,
            SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression or
            SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression or
            SyntaxKind.ExclusiveOrExpression => ExpressionPropagationMode.AggregateOnly,
            _ => ExpressionPropagationMode.AggregateOnly
        };
    }

    private static bool IsDirectOperand(BinaryExpressionSyntax binary, ExpressionSyntax expression) =>
        ReferenceEquals(binary.Left, expression) || ReferenceEquals(binary.Right, expression);

    private static bool IsDirectOperand(AssignmentExpressionSyntax assignment, ExpressionSyntax expression) =>
        ReferenceEquals(assignment.Left, expression) || ReferenceEquals(assignment.Right, expression);

    private static bool IsDirectOperand(ConditionalExpressionSyntax conditional, ExpressionSyntax expression) =>
      ReferenceEquals(conditional.Condition, expression) ||
      ReferenceEquals(conditional.WhenTrue, expression) ||
      ReferenceEquals(conditional.WhenFalse, expression);

    private static bool CanExposeStructuralOwners(IEnumerable<ExpressionTopologyStep> steps) =>
        !steps.Any(step => step.Host.IsKind(SyntaxKind.LogicalOrExpression));

    private static IReadOnlyList<SyntaxNode> BuildStructuralOwners(SyntaxNode start, bool includeStart)
    {
        var owners = new List<SyntaxNode>();
        for (SyntaxNode? current = includeStart ? start : start.Parent;
             current is not null;
             current = current.Parent)
        {
            owners.Add(current);
            if (current is StatementSyntax)
            {
                break;
            }
        }

        return owners;
    }

}
