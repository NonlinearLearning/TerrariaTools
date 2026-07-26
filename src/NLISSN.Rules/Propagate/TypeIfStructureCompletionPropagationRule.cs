using Microsoft.CodeAnalysis.CSharp;
using Deletion.Core.Marking;
using Deletion.Core.Propagation;

namespace Deletion.Rules;

public sealed class ClassIfStructureCompletionPropagationRule : RuleDefinitionPropagate
{
    public override string CapabilityId { get; } = "propagate.type.if-structure-completion";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IF-COMPLETE-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Propagate delete-class if/elseif/else completion state as structured payloads";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.ThisExpression,
        SyntaxKind.BaseExpression,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.SimpleAssignmentExpression,
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.DivideAssignmentExpression,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.TupleExpression,
        SyntaxKind.Block,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.ElseClause,
        SyntaxKind.IfStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.SwitchSection,
        SyntaxKind.ReturnStatement
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        return DeleteSObjectPropagationHelpers.EnumerateIfStructureCompletionPropagations(
          context,
          seedMarks,
          RuleId);
    }
}
