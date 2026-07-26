using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class ClassObjectCreationDeclarationPropagationRule : RuleDefinitionPropagate
{
    public override string CapabilityId { get; } = "propagate.type.object-creation-declaration";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NEW-DECL-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Propagate delete-class object creations to local declarators";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.VariableDeclarator
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ObjectCreationExpressionSyntax and
                not ImplicitObjectCreationExpressionSyntax)
            {
                continue;
            }

            var declarator = FindInitializerDeclarator(seedMark.SyntaxNode);
            if (declarator is null)
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                declarator,
                "Object creation initializer is marked; propagate mark to local declarator."),
              seedMark,
              1);
        }
    }

    private static VariableDeclaratorSyntax? FindInitializerDeclarator(SyntaxNode syntaxNode)
    {
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Value.Span.Contains(syntaxNode.Span) &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
            {
                return variableDeclarator;
            }
        }

        return null;
    }
}
