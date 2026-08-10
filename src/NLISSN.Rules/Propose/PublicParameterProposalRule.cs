using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class PublicParameterProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.type.public-parameter";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PUBLIC-PARAM-001";


    public override string Name { get; } = "Delete non-private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 当前对非私有参数整方法删除保持空操作，未支持情形交给诊断或更细粒度规则处理。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;
        _ = seedMarks;
        yield break;
    }

    private static bool TryResolveNonPrivateMethodFromParameter(TypeSyntax typeSyntax, out MethodDeclarationSyntax method)
    {
        var parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate =>
            candidate.Type?.Span.Contains(typeSyntax.Span) == true);
        method = (parameter?.Parent?.Parent as MethodDeclarationSyntax)!;
        if (method is null)
        {
            return false;
        }

        return MethodProposalSafety.IsSafeNonPrivateMethod(method);
    }
}

