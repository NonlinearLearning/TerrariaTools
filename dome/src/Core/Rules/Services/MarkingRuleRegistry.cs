namespace TerrariaTools.Dome.Core.Rules.Services;

/// <summary>
/// 聚合规则引擎运行所需的全部规则集合。
/// </summary>
public sealed class MarkingRuleRegistry
{
    /// <summary>
    /// 初始化规则注册表。
    /// </summary>
    public MarkingRuleRegistry(
        IEnumerable<ISeedRule> seedRules,
        IEnumerable<IExpressionProjectionRule> expressionProjectionRules,
        IEnumerable<IPropagationRule> propagationRules,
        IEnumerable<IProtectionRule> protectionRules,
        IEnumerable<IMethodRule> methodRules,
        IEnumerable<IMemberTargetRule> memberTargetRules,
        IEnumerable<IClassRule> classRules,
        IEnumerable<IBoundaryPromotionRule> boundaryPromotionRules,
        IEnumerable<IStatementScopeRule> statementScopeRules)
    {
        SeedRules = seedRules.ToArray();
        ExpressionProjectionRules = expressionProjectionRules.ToArray();
        PropagationRules = propagationRules.ToArray();
        ProtectionRules = protectionRules.ToArray();
        MethodRules = methodRules.ToArray();
        MemberTargetRules = memberTargetRules.ToArray();
        ClassRules = classRules.ToArray();
        BoundaryPromotionRules = boundaryPromotionRules.ToArray();
        StatementScopeRules = statementScopeRules.ToArray();
    }

    public IReadOnlyList<ISeedRule> SeedRules { get; }
    public IReadOnlyList<IExpressionProjectionRule> ExpressionProjectionRules { get; }
    public IReadOnlyList<IPropagationRule> PropagationRules { get; }
    public IReadOnlyList<IProtectionRule> ProtectionRules { get; }
    public IReadOnlyList<IMethodRule> MethodRules { get; }
    public IReadOnlyList<IMemberTargetRule> MemberTargetRules { get; }
    public IReadOnlyList<IClassRule> ClassRules { get; }
    public IReadOnlyList<IBoundaryPromotionRule> BoundaryPromotionRules { get; }
    public IReadOnlyList<IStatementScopeRule> StatementScopeRules { get; }

    /// <summary>
    /// 创建默认规则注册表。
    /// </summary>
    public static MarkingRuleRegistry CreateDefault()
    {
        ISeedRule[] seedRules = [new DirectiveSeedRule()];
        IExpressionProjectionRule[] expressionProjectionRules = [new ExpressionProjectionRule()];
        IPropagationRule[] propagationRules = [new SanitizationPropagationRule()];
        IProtectionRule[] protectionRules = [new HighRiskProtectionRule(), new ObjectInitializerProtectionRule()];
        IMethodRule[] methodRules = [new FunctionMarkingRule(), new PublicMethodPrivatizationRule(), new UnusedMethodRule()];
        IMemberTargetRule[] memberTargetRules = [new UnusedMemberRule()];
        IClassRule[] classRules = [new ClassMarkingRule(), new UnusedClassRule(), new PublicMethodOrderingRule()];
        IBoundaryPromotionRule[] boundaryPromotionRules = [new InvocationBoundaryPromotionRule()];
        IStatementScopeRule[] statementScopeRules = [new ParentBlockPiercingScopeRule()];

        return new MarkingRuleRegistry(
            seedRules,
            expressionProjectionRules,
            propagationRules,
            protectionRules,
            methodRules,
            memberTargetRules,
            classRules,
            boundaryPromotionRules,
            statementScopeRules);
    }
}
