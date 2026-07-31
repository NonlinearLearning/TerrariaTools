using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Composition;

/// Composition root 的默认规则管道目录。
public static class RuleRegistry
{
    // 汇总整条规则管道声明的 CPG 能力需求，供调用方决定要构哪些图能力。
    public static IReadOnlyList<NLCPG.Contracts.NLCPGCapability> GetRequiredCapabilities(RulePipeline pipeline)
    {
        return pipeline.GetRequiredCapabilities();
    }

    // 基于默认阶段列表创建稳定排序后的规则管道，并按名称排除禁用规则。
    public static RulePipeline CreateDefaultRules(IEnumerable<string>? disabledRuleTypes = null)
    {
        var markers = new RuleDefinitionMark[]
        {
            new AtomicIdentifierNameMarkRule(),
            new AtomicThisExpressionMarkRule(),
            new AtomicBaseExpressionMarkRule(),
            new AtomicVariableDeclaratorMarkRule(),
            new AtomicNumericLiteralMarkRule(),
            new AtomicStringLiteralMarkRule(),
            new AtomicTrueLiteralMarkRule(),
            new AtomicFalseLiteralMarkRule(),
            new AtomicNullLiteralMarkRule(),
            new AtomicMemberAccessMarkRule(),
            new AtomicMemberBindingMarkRule(),
            new AtomicInvocationMarkRule(),
            new AtomicObjectCreationMarkRule(),
            new AtomicImplicitObjectCreationMarkRule(),
            new AtomicElementAccessMarkRule(),
            new AtomicConditionalAccessMarkRule(),
            new DeclarationMarkRule(),
            new TypedExpressionMarkRule(),
            new TypeSyntaxMarkRule(),
            new UnreachableMethodMarkRule(),
            new UnreferencedMethodMarkRule(),
            new ClearUnusedInterfaceImplementationRule(),
            new PrivatizeInternalOnlyPublicMethodRule()
        }.ToList();
        var propagators = new RuleDefinitionPropagate[]
        {
            new AssignmentLeftValuePropagationRule(),
            new DefinitionInitializerPropagationRule(),
            new SymbolReferencePropagationRule(),
            new DeclarationHostPropagationRule(),
            new DelegateUsageClassificationPropagationRule(),
            new ExtensionMethodMappedCallsitePropagationRule(),
            new IndexerParameterUsagePropagationRule(),
            new LocalFunctionParameterUsagePropagationRule(),
            new MethodParameterUsagePropagationRule(),
            new ObjectCreationDeclarationPropagationRule(),
            new DeclarationSymbolReferencePropagationRule()
        }.ToList();
        var lifters = new RuleDefinitionLift[]
        {
            new ExpressionHostLiftingRule(),
            new LogicalExpressionLiftingRule(),
            new IfStructureLiftingRule(),
            new SwitchStructureLiftingRule(),
            new ControlStructureLiftingRule()
        }.ToList();
        var proposers = new RuleDefinitionPropose[]
        {
            new LogicalExpressionProposalRule(),
            new IfStructureProposalRule(),
            new ControlStructureRemovalProposalRule(),
            new DefaultRemovalProposalRule(),
            new TypeSyntaxDeclarationProposalRule(),
            new MethodReturnTypeProposalRule(),
            new PublicMethodReturnTypeProposalRule(),
            new ParameterProposalRule(),
            new PrivateMethodParameterShrinkProposalRule(),
            new NamedArgumentMethodParameterShrinkProposalRule(),
            new OptionalParameterDefaultedMethodShrinkProposalRule(),
            new PublicParameterProposalRule(),
            new ParamsMethodParameterShrinkProposalRule(),
            new PublicMethodParameterShrinkProposalRule(),
            new NamedArgumentLocalFunctionParameterShrinkProposalRule(),
            new OptionalParameterDefaultedLocalFunctionShrinkProposalRule(),
            new LocalFunctionParameterShrinkProposalRule(),
            new NamedArgumentIndexerParameterShrinkProposalRule(),
            new IndexerParameterShrinkProposalRule(),
            new DelegateParameterShrinkProposalRule(),
            new MethodGroupDelegateParameterShrinkProposalRule(),
            new LambdaDelegateParameterShrinkProposalRule(),
            new DelegateInvocationChainParameterShrinkProposalRule(),
            new ExtensionReceiverNonFirstParameterShrinkProposalRule(),
            new InterfaceMethodProposalRule(),
            new InterfacePropertyProposalRule(),
            new InterfaceEventProposalRule(),
            new InterfaceIndexerProposalRule(),
            new DelegateProposalRule(),
            new ExtensionReceiverProposalRule(),
            new BaseTypeProposalRule(),
            new GenericTypeArgumentProposalRule(),
            new UnreachableMethodProposalRule(),
            new UnreferencedMethodProposalRule(),
            new ClearUnusedInterfaceImplementationProposalRule(),
            new PrivatizeInternalOnlyPublicMethodProposalRule()
        }.ToList();

        RuleCatalog.ValidateRules(markers.Cast<IRuleDefinition>()
          .Concat(propagators)
          .Concat(lifters)
          .Concat(proposers));
        var disabledTypeNames = (disabledRuleTypes ?? Array.Empty<string>())
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new RulePipeline(
          Markers: CreateRules(markers, disabledTypeNames),
          Propagators: CreateRules(propagators, disabledTypeNames),
          Lifters: CreateRules(lifters, disabledTypeNames),
          Proposers: CreateRules(proposers, disabledTypeNames),
          DisabledMarkers: FindDisabled(markers, disabledTypeNames),
          DisabledPropagators: FindDisabled(propagators, disabledTypeNames),
          DisabledLifters: FindDisabled(lifters, disabledTypeNames),
          DisabledProposers: FindDisabled(proposers, disabledTypeNames));
    }

    private static IReadOnlyList<TRule> CreateRules<TRule>(IEnumerable<TRule> rules, IReadOnlySet<string> disabledTypeNames)
      where TRule : class
    {
        return rules
          .Where(rule => !disabledTypeNames.Contains(rule.GetType().Name))
          .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
          .ToList();
    }

    private static IReadOnlyList<TRule> FindDisabled<TRule>(
      IEnumerable<TRule> rules,
      IReadOnlySet<string> disabledTypeNames)
      where TRule : class
    {
        return rules
          .Where(rule => disabledTypeNames.Contains(rule.GetType().Name))
          .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
          .ToList();
    }
}
