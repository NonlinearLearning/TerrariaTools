using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public abstract class ClassDeclarationHostProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag DeclarationHostSemanticTag = new("Class.DeclarationHost");

    private static readonly RuleConsumesContract DeclarationHostConsumes =
      RuleStructureContractFactories.CreateDeclarationHostConsumes(
        DeclarationHostSemanticTag,
        RuleInputCardinality.All);

    public override RuleConsumesContract Consumes => DeclarationHostConsumes;
}

public abstract class ClassMethodParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag MethodParameterUsageSemanticTag = new("Class.MethodParameterUsage");

    private static readonly RuleConsumesContract MethodParameterUsageConsumes =
      RuleStructureContractFactories.CreateMethodParameterUsageConsumes(
        MethodParameterUsageSemanticTag,
        RuleInputCardinality.All);

    public override RuleConsumesContract Consumes => MethodParameterUsageConsumes;
}

public abstract class ClassLocalFunctionParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag LocalFunctionParameterUsageSemanticTag = new("Class.LocalFunctionParameterUsage");

    private static readonly RuleConsumesContract LocalFunctionParameterUsageConsumes =
      RuleStructureContractFactories.CreateLocalFunctionParameterUsageConsumes(
        LocalFunctionParameterUsageSemanticTag,
        RuleInputCardinality.All);

    public override RuleConsumesContract Consumes => LocalFunctionParameterUsageConsumes;
}

public abstract class ClassIndexerParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag IndexerParameterUsageSemanticTag = new("Class.IndexerParameterUsage");

    private static readonly RuleConsumesContract IndexerParameterUsageConsumes =
      RuleStructureContractFactories.CreateIndexerParameterUsageConsumes(
        IndexerParameterUsageSemanticTag,
        RuleInputCardinality.All);

    public override RuleConsumesContract Consumes => IndexerParameterUsageConsumes;
}

public abstract class ClassDelegateUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag DelegateUsageSemanticTag = new("Class.DelegateUsage");

    private static readonly RuleConsumesContract DelegateUsageConsumes =
      RuleStructureContractFactories.CreateDelegateUsageConsumes(
        DelegateUsageSemanticTag,
        RuleInputCardinality.All);

    public override RuleConsumesContract Consumes => DelegateUsageConsumes;
}

public abstract class ClassExtensionMethodParameterUsageProposalRuleBase : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag ExtensionMethodParameterUsageSemanticTag = new("Class.ExtensionMethodParameterUsage");

    private static readonly RuleConsumesContract ExtensionMethodParameterUsageConsumes =
      RuleStructureContractFactories.CreateExtensionMethodParameterUsageConsumes(
        ExtensionMethodParameterUsageSemanticTag,
        RuleInputCardinality.All);

    public override RuleConsumesContract Consumes => ExtensionMethodParameterUsageConsumes;
}

/// 默认处理未被专门规则接管的类删除标记，避免与结构或声明宿主决策重叠。
public sealed class ClassDefaultRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleTerminalConsumesContract AllClassFacts = new(
      new[]
      {
        new RuleTerminalFactSelector(
          RuleFactDomain.Class,
          new[] { RuleKind.Mark, RuleKind.Propagate, RuleKind.Lift })
      });

    public override string CapabilityId { get; } = "propose.type.default-removal";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DEFAULT-001";

    public override RuleTerminalConsumesContract TerminalConsumes => AllClassFacts;


    public override string Name { get; } = "Match delete-class default delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.DefaultConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为未被专门规则接管的删除类 mark 生成默认删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(mark, sourceMark))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }

        foreach (var seedMark in DeleteSObjectProposalHelpers.EnumerateUncoveredSeedMarks(
                     seedMarks,
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(seedMark))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }
    }

    private static bool IsHandledBySpecializedRule(MarkRecord mark)
    {
        var kind = (SyntaxKind)mark.SyntaxNode.RawKind;
        return DeleteSObjectProposalHelpers.IfConflictNodeKinds.Contains(kind) ||
          DeleteSObjectProposalHelpers.ControlConflictNodeKinds.Contains(kind) ||
          mark.SyntaxNode is TypeSyntax ||
          mark.SyntaxNode is ElseClauseSyntax;
    }

    private static bool IsHandledBySpecializedRule(MarkRecord mark, MarkRecord sourceMark)
    {
        return IsHandledBySpecializedRule(mark) ||
          (sourceMark.RuleId == "DEL-CLASS-PROP-DECL-HOST-001") ||
          (mark.SyntaxNode is LocalDeclarationStatementSyntax &&
           sourceMark.SyntaxNode is TypeSyntax);
    }
}

/// 仅为传播确认的控制结构宿主生成删除决策。
public sealed class ClassControlStructureRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleTerminalConsumesContract AllClassFacts = new(
      new[]
      {
        new RuleTerminalFactSelector(
          RuleFactDomain.Class,
          new[] { RuleKind.Mark, RuleKind.Propagate, RuleKind.Lift })
      });

    public override string CapabilityId { get; } = "propose.type.control-structure-removal";

    public override string RuleId { get; } = "DEL-CLASS-PROP-CTRL-001";

    public override RuleTerminalConsumesContract TerminalConsumes => AllClassFacts;


    public override string Name { get; } = "Match delete-class control structure delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.ControlConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 把删除类链路中已提升到控制结构宿主的 mark 转成直接删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            var kind = (SyntaxKind)mark.SyntaxNode.RawKind;
            if (!DecisionConflictNodeKinds.Contains(kind))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }
    }
}

/// 将 TypeSyntax 标记提升为包含该类型语法的声明改写。
public sealed class ClassTypeSyntaxDeclarationProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.type-syntax-declaration";

    public override string RuleId { get; } = "DEL-CLASS-PROP-TYPE-DECL-001";


    public override string Name { get; } = "Delete declarations whose type syntax references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.FieldDeclaration,
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 把字段和属性上的声明宿主 payload 直接落成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.FieldDeclaration))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Declaration type references the delete-class target.",
              payload.HostDeclaration);
        }

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.PropertyDeclaration))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Declaration type references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

/// 删除可安全替换的方法返回类型，并保留声明主体。
public sealed class ClassMethodReturnTypeProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.method-return-type";

    public override string RuleId { get; } = "DEL-CLASS-PROP-RETURN-001";


    public override string Name { get; } = "Delete private methods whose return type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为返回类型引用目标类的私有方法生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.MethodReturnType))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method ||
                !DeleteClassMethodProposalSafety.IsSafePrivateMethod(method))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Private method return type references the delete-class target.",
              method);
        }
    }
}

/// 处理公开方法返回类型，要求调用与重载绑定不会因替换而漂移。
public sealed class ClassPublicMethodReturnTypeProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.public-method-return-type";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PUBLIC-RETURN-001";


    public override string Name { get; } = "Delete non-private methods whose return type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为返回类型引用目标类的非私有方法生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.MethodReturnType))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method ||
                !DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(method))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Non-private method return type references the delete-class target.",
              method);
        }
    }
}

/// 保留旧参数删除入口的规则标识；具体收缩由更严格的专门规则完成。
public sealed class ClassParameterProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.type.parameter";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PARAM-001";


    public override string Name { get; } = "Delete private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 当前刻意不为“整方法参数删除”直接产出决策，避免与更细粒度的收缩规则冲突。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;
        _ = seedMarks;
        yield break;
    }
}

/// 删除私有方法参数，并同步改写可证明的稳定位置调用。
public sealed class ClassPrivateMethodParameterShrinkProposalRule : ClassMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.private-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PRIVATE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩私有方法声明和所有稳定位置调用点的目标参数。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PrivatePositional))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 处理具名实参方法调用，按参数符号而非位置删除对应实参。
public sealed class ClassNamedArgumentMethodParameterShrinkProposalRule : ClassMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-METHOD-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink method parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名参数调用同步收缩方法声明与命名实参。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.NamedArgument))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Method parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Named argument passes the deleted class type value; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 删除可省略的可选参数，同时维持省略调用的绑定语义。
public sealed class ClassOptionalParameterDefaultedMethodShrinkProposalRule : ClassMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.optional-parameter-defaulted-method-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-OPTIONAL-METHOD-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink optional method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 收缩带默认值的方法参数，并只在需要时改写显式传参调用点。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.Optional))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Optional method parameter type references the delete-class target; shrink the signature and keep omitted callsites unchanged.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Optional parameter is explicitly passed with the deleted class type value; remove the matching argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 对未被已验证收缩路径覆盖的公开参数保持保守，不生成整方法删除决策。
public sealed class ClassPublicParameterProposalRule : RuleDefinitionPropose
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
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 当前对非私有参数整方法删除保持空操作，未支持情形交给诊断或更细粒度规则处理。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
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

        return DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(method);
    }
}

/// 仅收缩末尾 params 参数，并拒绝显式数组或展开形状不确定的调用。
public sealed class ClassParamsMethodParameterShrinkProposalRule : ClassMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.params-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PARAMS-METHOD-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink params method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 仅在所有调用都省略 params 槽位时，收缩 params 方法声明。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.ParamsOmitted))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Params method parameter type references the delete-class target; shrink the signature when all callsites omit the params slot.");
        }
    }
}

/// 仅在收集到全部调用点时收缩非私有方法参数。
public sealed class ClassPublicMethodParameterShrinkProposalRule : ClassMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.public-method-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-PUBLIC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink non-private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 为非私有方法同步收缩声明和可证明完整覆盖的调用点。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassMethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PublicPositional))
        {
            if (!DeleteClassMethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Non-private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassMethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 处理局部函数的具名实参，并与局部声明同步替换。
public sealed class ClassNamedArgumentLocalFunctionParameterShrinkProposalRule : ClassLocalFunctionParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-local-function-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-LOCALFUNC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target when callsites use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名参数局部函数调用，同步收缩声明与命名实参。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassLocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.NamedArgument))
        {
            if (!DeleteClassLocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature for named-argument callsites.");

            foreach (var decision in DeleteClassLocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation passes the deleted class type value by name; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 删除局部函数的可选参数，同时保留省略实参调用的合法性。
public sealed class ClassOptionalParameterDefaultedLocalFunctionShrinkProposalRule : ClassLocalFunctionParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.optional-parameter-defaulted-local-function-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-OPTIONAL-LOCALFUNC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink optional local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 收缩带默认值的局部函数参数，并只改写确实需要调整的调用点。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassLocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Optional))
        {
            if (!DeleteClassLocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Optional local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassLocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation explicitly passes the deleted class type optional argument; remove that argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 删除局部函数参数并改写同一作用域内可绑定的调用点。
public sealed class ClassLocalFunctionParameterShrinkProposalRule : ClassLocalFunctionParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.local-function-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-LOCALFUNC-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink local function parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 为普通位置参数局部函数同步收缩声明与调用点。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassLocalFunctionUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     LocalFunctionParameterUsageMode.Positional))
        {
            if (!DeleteClassLocalFunctionUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementLocalFunction))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
              RuleId,
              payload.LocalFunction,
              replacementLocalFunction,
              "Local function parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassLocalFunctionUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Local function invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 处理索引器具名参数的声明与元素访问同步收缩。
public sealed class ClassNamedArgumentIndexerParameterShrinkProposalRule : ClassIndexerParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-indexer-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-INDEXER-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target when accesses use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名索引实参同步收缩索引器声明与 element access。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassIndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.NamedArgument))
        {
            if (!DeleteClassIndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature for named-argument accesses.");

            foreach (var decision in DeleteClassIndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Indexer access passes the deleted class type value by name; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 仅在所有元素访问都可安全重写时收缩索引器参数。
public sealed class ClassIndexerParameterShrinkProposalRule : ClassIndexerParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.indexer-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-INDEXER-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对位置索引实参同步收缩索引器声明与 element access。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassIndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.Positional))
        {
            if (!DeleteClassIndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in DeleteClassIndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Indexer access passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

/// 处理没有方法组或 lambda 绑定链的简单委托参数删除。
public sealed class ClassDelegateParameterShrinkProposalRule : ClassDelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.delegate-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink delegate parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 在委托没有外部复杂绑定时，只收缩委托签名本身。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.PlainSignature))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the signature.");
        }
    }
}

/// 收缩委托参数并同步改写经方法组绑定的目标方法。
public sealed class ClassMethodGroupDelegateParameterShrinkProposalRule : ClassDelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.method-group-delegate-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-METHODGROUP-DELEGATE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink delegate parameters and method-group targets when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩委托签名以及所有 method group 绑定与调用链。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.MethodGroup))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its method-group targets.");

            foreach (var method in payload.MethodTargets)
            {
                var methodParameter = method.ParameterList.Parameters.ElementAtOrDefault(payload.ParameterIndex);
                if (methodParameter is null ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildReplacementMethod(
                      method,
                      methodParameter,
                      out var replacementMethod))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
                  RuleId,
                  method,
                  replacementMethod,
                  "Method group target now removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var localFunction in payload.LocalFunctionTargets)
            {
                var parameter = localFunction.ParameterList.Parameters.ElementAtOrDefault(payload.ParameterIndex);
                if (parameter is null ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildReplacementLocalFunction(
                      localFunction,
                      parameter,
                      out var replacementLocalFunction))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateLocalFunctionReplaceDecision(
                  RuleId,
                  localFunction,
                  replacementLocalFunction,
                  "Local-function method group target now removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

/// 收缩委托参数并同步改写依赖该签名的 lambda 形参。
public sealed class ClassLambdaDelegateParameterShrinkProposalRule : ClassDelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.lambda-delegate-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-LAMBDA-DELEGATE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink delegate parameters and lambda bindings when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.AnonymousMethodExpression,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩委托签名以及所有 lambda 绑定与调用链。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.Lambda))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its lambda bindings.");

            foreach (var expression in payload.LambdaTargets)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
                if (model.GetOperation(expression, CancellationToken.None) is not IAnonymousFunctionOperation anonymousFunction ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildLambdaRewrite(
                      context,
                      model,
                      expression,
                      anonymousFunction,
                      payload.ParameterIndex,
                      out var lambdaRewrite))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateExpressionReplaceDecision(
                  RuleId,
                  lambdaRewrite.Expression,
                  lambdaRewrite.Replacement,
                  "Lambda binding removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

/// 覆盖直接委托调用链，确保声明、绑定和调用实参一起收缩。
public sealed class ClassDelegateInvocationChainParameterShrinkProposalRule : ClassDelegateUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.delegate-invocation-chain-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-INVOKE-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink delegate parameters and direct delegate invocation chains when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩委托签名以及直接委托调用链上的实参。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.InvocationChain))
        {
            if (!DeleteClassDelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its direct invocation chain.");

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

/// 只收缩扩展方法的非接收者参数；接收者参数受调用形式约束而保留。
public sealed class ClassExtensionReceiverNonFirstParameterShrinkProposalRule : ClassExtensionMethodParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.extension-receiver-non-first-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-EXT-NONRECV-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink non-receiver extension-method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 保持扩展方法接收者不变，只收缩非首个目标参数及其映射调用点。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassExtensionMethodUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks))
        {
            if (!DeleteClassExtensionMethodUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Extension method non-receiver parameter type references the delete-class target; shrink the signature and keep the receiver.");

            foreach (var decision in DeleteClassExtensionMethodUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Extension method invocation removes the deleted class type argument while preserving the receiver."))
            {
                yield return decision;
            }
        }
    }
}

/// 将接口方法签名中的目标类型删除映射为接口成员声明改写。
public sealed class ClassInterfaceMethodProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-method";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-METHOD-001";


    public override string Name { get; } = "Delete interface methods whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为接口方法签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceMethod))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Interface method signature references the delete-class target.",
              method);
        }
    }
}

/// 将接口属性签名中的目标类型删除映射为属性声明改写。
public sealed class ClassInterfacePropertyProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-property";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-PROPERTY-001";


    public override string Name { get; } = "Delete interface properties whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为接口属性签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceProperty))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface property signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

/// 将接口事件签名中的目标类型删除映射为事件声明改写。
public sealed class ClassInterfaceEventProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-event";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-EVENT-001";


    public override string Name { get; } = "Delete interface events whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为接口事件签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceEvent))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface event signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

/// 将接口索引器签名中的目标类型删除映射为索引器声明改写。
public sealed class ClassInterfaceIndexerProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.interface-indexer";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IFACE-INDEXER-001";


    public override string Name { get; } = "Delete interface indexers whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为接口索引器签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceIndexer))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface indexer signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

/// 处理委托返回类型删除；参数删除由参数收缩专门规则承担。
public sealed class ClassDelegateProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.delegate";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-001";


    public override string Name { get; } = "Delete delegates whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为委托返回类型上的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.DelegateReturnType))
        {
            if (payload.HostDeclaration is not DelegateDeclarationSyntax delegateDeclaration)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              delegateDeclaration,
              "Delegate signature references the delete-class target.",
              delegateDeclaration);
        }
    }
}

/// 处理扩展接收者类型删除，并避免把实例调用改写为不等价形式。
public sealed class ClassExtensionReceiverProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.extension-receiver";

    public override string RuleId { get; } = "DEL-CLASS-PROP-EXT-RECV-001";


    public override string Name { get; } = "Delete extension methods whose receiver type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为接收者类型命中目标类的扩展方法直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.ExtensionReceiverMethod))
        {
            if (payload.HostDeclaration is not MethodDeclarationSyntax method)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              method,
              "Extension method receiver type references the delete-class target.",
              method);
        }
    }
}

/// 删除基类型列表中已标记的目标类型，保留其余继承和接口项。
public sealed class ClassBaseTypeProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.base-type";

    public override string RuleId { get; } = "DEL-CLASS-PROP-BASE-001";


    public override string Name { get; } = "Remove base-list entries whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.BaseList,
        SyntaxKind.SimpleBaseType
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为基类或接口列表中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.BaseType))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Base type references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

/// 处理泛型局部声明中的目标类型实参，且只在宿主语法可完整替换时提出决策。
public sealed class ClassGenericTypeArgumentProposalRule : ClassDeclarationHostProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.generic-type-argument";

    public override string RuleId { get; } = "DEL-CLASS-PROP-GENERIC-001";


    public override string Name { get; } = "Delete local declarations whose generic type argument references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalDeclarationStatement
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为局部泛型声明中引用目标类的类型实参直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteClassDeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.LocalGenericTypeArgument))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Local declaration type argument references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

/// 将类删除传播出的完整条件结构转换为结构化改写决策。
public sealed class ClassIfStructureProposalRule : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag IfCompletionSemanticTag = new("Class.IfCompletion");

    private static readonly RuleConsumesContract IfCompletionConsumes =
      RuleStructureContractFactories.CreateIfCompletionConsumes(
        IfCompletionSemanticTag,
        RuleInputCardinality.All);

    public override string CapabilityId { get; } = "propose.type.if-structure";

    public override string RuleId { get; } = "DEL-CLASS-PROP-IF-001";


    public override string Name { get; } = "Match delete-class if/elseif/else structure decisions";

    public override RuleConsumesContract Consumes => IfCompletionConsumes;

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.IfConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 把删除类链路的 if 完成态 payload 规约成唯一结构改写决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;
        var consumedKeys = new HashSet<(int Start, int Length, int RawKind)>();

        foreach (var payload in DeleteSObjectProposalHelpers.EnumerateIfStructureCompletionPayloads(
                     propagatedMarks))
        {
            var decisionNode = DeleteSObjectProposalHelpers.GetIfStructureDecisionNode(payload);

            if (consumedKeys.Contains(DeleteSObjectProposalHelpers.BuildNodeKey(decisionNode)))
            {
                continue;
            }

            if (DeleteSObjectProposalHelpers.TryBuildIfStructureDecisionFromMark(
                    RuleId,
                    payload,
                    out var decision,
                    out var consumedNodes) &&
                decision is not null)
            {
                foreach (var node in consumedNodes)
                {
                    consumedKeys.Add(DeleteSObjectProposalHelpers.BuildNodeKey(node));
                }

                yield return decision;
            }
        }
    }
}
