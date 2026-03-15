namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Core;

/// 绉嶅瓙瑙勫垯鎺ュ彛銆?
/// </summary>
public interface ISeedRule
{
    /// <summary>
    /// 璇勪及鐩爣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisTarget target);
}

/// 浼犳挱瑙勫垯鎺ュ彛銆?
/// </summary>
public interface IPropagationRule
{
    /// <summary>
    /// 妫€鏌ユ槸鍚﹀彲浠ヤ紶鎾€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <param name="usedSymbol">浣跨敤鐨勭鍙枫€?/param>
    /// <param name="sourceDecision">婧愬喅绛栥€?/param>
    /// <returns>濡傛灉鍙互浼犳挱鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
    bool CanPropagate(AnalysisTarget target, SymbolRef usedSymbol, MarkDecision sourceDecision);
}

/// <summary>
/// 淇濇姢瑙勫垯鎺ュ彛銆?
/// </summary>
public interface IProtectionRule
{
    /// <summary>
    /// 妫€鏌ユ槸鍚﹂樆姝㈡爣璁般€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>濡傛灉闃绘鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
    bool Blocks(AnalysisTarget target);
}

/// <summary>
/// 琛ㄨ揪寮忔姇褰辫鍒欐帴鍙ｃ€?
/// </summary>
public interface IExpressionProjectionRule
{
    /// <summary>
    /// 璇勪及鐩爣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisTarget target);
}

/// <summary>
/// 鏂规硶瑙勫垯鎺ュ彛銆?
/// </summary>
public interface IMethodRule
{
    /// <summary>
    /// 璇勪及鍑芥暟鑺傜偣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="context">鍒嗘瀽涓婁笅鏂囥€?/param>
    /// <param name="functionNode">鍑芥暟鑺傜偣寮曠敤銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, FunctionNodeRef functionNode);
}

/// <summary>
/// 绫昏鍒欐帴鍙ｃ€?
/// </summary>
public interface IClassRule
{
    /// <summary>
    /// 璇勪及鍒嗘瀽鐩爣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="context">鍒嗘瀽涓婁笅鏂囥€?/param>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target);
}

/// <summary>
/// 杈圭晫鎻愬崌瑙勫垯鎺ュ彛銆?
/// </summary>
public interface IBoundaryPromotionRule
{
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target, MarkDecision decision);
}

/// <summary>
/// 璇彞浣滅敤鍩熼€夋嫨瑙勫垯鎺ュ彛銆?
/// </summary>
public interface IStatementScopeRule
{
    StatementScopeMode SelectScopeMode(AnalysisContext context, AnalysisTarget seedTarget);
}

/// <summary>
/// 鎸囦护绉嶅瓙瑙勫垯銆?
/// 瑙勫垯鏃忥細ISeedRule銆?
/// 鍛戒腑鐩爣锛氭惡甯?dome 鎸囦护鐨?statement target锛涙帶鍒舵祦璇彞浼氱粺涓€涓?controlflow-mark銆?
/// 鐩存帴浜у嚭锛歴tatement 绾?MarkDecision锛宎ction 鍙栬嚜 directive锛宺eason 涓?directive rule id銆?
/// 浼犳挱锛氭槸锛岀洿鎺ョ瀛愬喅绛栧彲娌?statement snapshot 鐨?use/def 杈圭户缁紶鎾€?
/// 闃绘柇锛歴anitization銆乧lean redefinition銆乸rotection rule銆乻cope boundary銆?
/// 鎻愬崌锛氭槸锛宒irect statement delete 浠嶅彲杩涘叆 boundary promotion銆?
/// 鏈€灏忔祴璇曪細
/// - DirectiveSeedRule_MarksStatementWithDeleteDirective
/// - DirectiveSeedRule_DoesNotMarkStatementWithoutDirective
/// </summary>
public sealed class DirectiveSeedRule : ISeedRule
{
    /// <summary>
    /// 璇勪及鐩爣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisTarget target)
    {
        if (target.HasMarkedExpressionSeed && !IsControlFlowTarget(target.StatementKind))
        {
            yield break;
        }

        foreach (var directive in target.Directives)
        {
            var ruleId = IsControlFlowTarget(target.StatementKind)
                ? "controlflow-mark"
                : directive.RuleId;
            var reasonText = ruleId == "controlflow-mark"
                ? "Directive matched a control-flow target."
                : directive.ReasonText;

            yield return MarkDecision.ForTarget(
                target.Target,
                directive.ActionKind,
                ruleId,
                reasonText,
                directive.Payload,
                origin: DecisionOrigin.Seed);
        }
    }

    private static bool IsControlFlowTarget(StatementKindRef statementKind) =>
        statementKind is StatementKindRef.If or StatementKindRef.While or StatementKindRef.For or StatementKindRef.Return;
}

/// <summary>
/// 琛ㄨ揪寮忔姇褰辫鍒欍€?
/// </summary>
/// <summary>
/// 琛ㄨ揪寮忔姇褰辫鍒欍€?
/// 瑙勫垯鏃忥細IExpressionProjectionRule銆?
/// 鍛戒腑鐩爣锛氬甫 directive 涓?HasMarkedExpressionSeed 涓?true 鐨勯潪鎺у埗娴?statement target銆?
/// 鐩存帴浜у嚭锛歴tatement 绾?MarkDecision锛宺eason 鍥哄畾涓?expression-mark銆?
/// 浼犳挱锛氭槸锛屾姇褰辩粨鏋滄寜 direct decision 澶勭悊銆?
/// 闃绘柇锛氶珮椋庨櫓 target銆佸璞″垵濮嬪寲璧嬪€笺€侀敊璇殑 statement 褰掑睘銆乻cope boundary銆?
/// 鎻愬崌锛氭槸锛屾姇褰变骇鐢熺殑 statement delete 浠嶅睘浜庡彲鎻愬崌鐨?direct delete銆?
/// 鏈€灏忔祴璇曪細
/// - ExpressionProjectionRule_ProjectsDeleteToContainingStatement
/// - ExpressionProjectionRule_DoesNotProjectAcrossDifferentStatement
/// </summary>
public sealed class ExpressionProjectionRule : IExpressionProjectionRule
{
    /// <summary>
    /// 璇勪及鐩爣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisTarget target)
    {
        if (!target.HasMarkedExpressionSeed ||
            target.Directives.Count == 0 ||
            target.Target.TargetKind != TargetKind.Statement ||
            target.StatementKind is StatementKindRef.If or StatementKindRef.While or StatementKindRef.For or StatementKindRef.Return ||
            target.IsHighRisk ||
            target.IsObjectInitializerAssignment)
        {
            yield break;
        }

        foreach (var directive in target.Directives)
        {
            yield return MarkDecision.ForTarget(
                target.Target,
                directive.ActionKind,
                "expression-mark",
                "Directive matched an expression-bearing statement and was projected to the statement target.",
                directive.Payload,
                relatedSymbolNames: target.MarkedExpressionKinds,
                origin: DecisionOrigin.Projection);
        }
    }
}

/// <summary>
/// 娓呯悊浼犳挱瑙勫垯銆?
/// </summary>
/// <summary>
/// 娓呮礂浼犳挱瑙勫垯銆?
/// 瑙勫垯鏃忥細IPropagationRule銆?
/// 鍛戒腑鐩爣锛氫紶鎾亶鍘嗚繃绋嬩腑琚瘑鍒负 sanitizing assignment 鐨勮妭鐐广€?
/// 鐩存帴浜у嚭锛氭棤锛涜瑙勫垯鍙礋璐ｇ粓姝紶鎾€?
/// 浼犳挱锛氫笉閫傜敤锛涘畠涓嶆槸浼犳挱婧愯鍒欍€?
/// 闃绘柇锛氫换鎰?sanitizing assignment 閮戒細缁堟缁忚繃璇ヨ妭鐐圭殑浼犳挱銆?
/// 鎻愬崌锛氬惁銆?
/// 鏈€灏忔祴璇曪細
/// - SanitizationPropagationRule_StopsPropagationAfterSanitizingAssignment
/// </summary>
public sealed class SanitizationPropagationRule : IPropagationRule
{
    /// <summary>
    /// 妫€鏌ユ槸鍚﹀彲浠ヤ紶鎾€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <param name="usedSymbol">浣跨敤鐨勭鍙枫€?/param>
    /// <param name="sourceDecision">婧愬喅绛栥€?/param>
    /// <returns>濡傛灉鍙互浼犳挱鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
    public bool CanPropagate(AnalysisTarget target, SymbolRef usedSymbol, MarkDecision sourceDecision)
    {
        return !target.IsSanitizingAssignment;
    }
}

/// <summary>
/// 楂橀闄╀繚鎶よ鍒欍€?
/// </summary>
/// <summary>
/// 楂橀闄╀繚鎶よ鍒欍€?
/// 瑙勫垯鏃忥細IProtectionRule銆?
/// 鍛戒腑鐩爣锛氳鍒嗘瀽缁撴灉鏍囪涓洪珮椋庨櫓鐨?target銆?
/// 鐩存帴浜у嚭锛氭棤锛涜瑙勫垯浼氬悓鏃堕樆姝?direct mark 鍜?propagation銆?
/// 浼犳挱锛氬惁锛涜淇濇姢 target 鍚屾椂鏄紶鎾竟鐣屻€?
/// 闃绘柇锛氫竴鏃﹀懡涓紝褰撳墠 target 琚烦杩囷紝taint 涓嶅緱缁х画绌胯繃璇ヨ妭鐐广€?
/// 鎻愬崌锛氬惁銆?
/// 鏈€灏忔祴璇曪細
/// - HighRiskProtectionRule_BlocksPropagationIntoProtectedTarget
/// </summary>
public sealed class HighRiskProtectionRule : IProtectionRule
{
    /// <summary>
    /// 妫€鏌ユ槸鍚﹂樆姝㈡爣璁般€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>濡傛灉闃绘鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
    public bool Blocks(AnalysisTarget target) => target.IsHighRisk;
}

/// <summary>
/// 瀵硅薄鍒濆鍖栧櫒淇濇姢瑙勫垯銆?
/// </summary>
/// <summary>
/// 瀵硅薄鍒濆鍖栧櫒淇濇姢瑙勫垯銆?
/// 瑙勫垯鏃忥細IProtectionRule銆?
/// 鍛戒腑鐩爣锛氳璇嗗埆涓?object initializer assignment 鐨?statement target銆?
/// 鐩存帴浜у嚭锛氭棤锛涜瑙勫垯闃绘瀵硅薄鍒濆鍖栬祴鍊艰鐩存帴鏍囪銆?
/// 浼犳挱锛氬惁锛涜淇濇姢鐨勫璞″垵濮嬪寲璇彞鍚屾椂鏄紶鎾竟鐣屻€?
/// 闃绘柇锛氫竴鏃﹀懡涓紝褰撳墠 target 琚烦杩囷紝taint 涓嶅緱缁х画绌胯繃璇ヨ妭鐐广€?
/// 鎻愬崌锛氬惁銆?
/// 鏈€灏忔祴璇曪細
/// - ObjectInitializerProtectionRule_DoesNotMarkInitializerAssignment
/// </summary>
public sealed class ObjectInitializerProtectionRule : IProtectionRule
{
    /// <summary>
    /// 妫€鏌ユ槸鍚﹂樆姝㈡爣璁般€?
    /// </summary>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>濡傛灉闃绘鍒欒繑鍥?true锛屽惁鍒欒繑鍥?false銆?/returns>
    public bool Blocks(AnalysisTarget target) => target.IsObjectInitializerAssignment;
}

/// <summary>
/// 鍑芥暟鏍囪瑙勫垯銆?
/// </summary>
/// <summary>
/// 鍑芥暟鏍囪瑙勫垯銆?
/// 瑙勫垯鏃忥細IMethodRule銆?
/// 鍛戒腑鐩爣锛氭湭琚?override/interface 璇箟淇濇姢鐨勭鏈夋柟娉曘€?
/// 鐩存帴浜у嚭锛?
/// - 鏃犲紩鐢ㄧ殑绉佹湁鏂规硶浜у嚭 method 绾?Delete锛宺eason 涓?function-mark
/// - 宸茶寮曠敤浣嗛潪 void 涓旂┖浣撶殑鏂规硶浜у嚭 method 绾?AddReturn
/// 浼犳挱锛氫笉閫傜敤銆?
/// 渚濇嵁锛欶unctionIndex銆両InheritanceQueryService銆両ReferenceQueryService銆?
/// 闃绘柇锛歰verride銆乮nterface implementation銆佸凡鐭ユ鏋跺叆鍙ｄ繚鎶ゃ€佷粛鏈夊紩鐢ㄣ€?
/// 鎻愬崌锛氬惁銆?
/// 鏈€灏忔祴璇曪細
/// - Execute_EmitsDeleteForUnreferencedPrivateMethod
/// - Execute_EmitsAddReturnForReferencedEmptyNonVoidMethod
/// - Execute_DoesNotDeleteOverrideMethod
/// - Execute_DoesNotDeleteEventSubscribedPrivateMethod
/// - Execute_DoesNotDeleteDelegateAssignedPrivateMethod
/// - Execute_DoesNotDeleteMethodCachedInDelegateDictionary
/// - Execute_DoesNotDeleteKnownFrameworkEntrypointMethod
/// - Execute_DoesNotDeleteKnownFrameworkShutdownMethod
/// - Execute_DoesNotDeleteKnownFrameworkDrawMethod
/// - Execute_DoesNotDeleteKnownFrameworkApplyPassMethod
/// - Execute_DeletesUnreferencedPrivateHelperInsideRegisteredType
/// - Execute_DeletesSameNamedMethodOutsideKnownFrameworkType
/// </summary>
public sealed class FunctionMarkingRule : IMethodRule
{
    private static readonly HashSet<string> KnownFrameworkEntrypointNames =
        new(StringComparer.Ordinal)
        {
            "Initialize",
            "Shutdown",
            "Connect",
            "Send",
            "Receive",
            "StartListening",
            "Apply",
            "ApplyPass",
            "TryDroppingItem",
            "CanDrop",
            "Find",
            "Perform",
            "Load",
            "OnBegin",
            "OnEnd",
            "OnActivate",
            "Draw",
            "Recalculate"
        };

    private static readonly string[] KnownFrameworkTypeMarkers =
    [
        "GenPass",
        "GenShape",
        "GenAction",
        "GenStructure",
        "NetSocialModule",
        "IItemDropRule",
        "ISocialModule"
    ];

    /// <summary>
    /// 璇勪及鍑芥暟鑺傜偣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="context">鍒嗘瀽涓婁笅鏂囥€?/param>
    /// <param name="functionNode">鍑芥暟鑺傜偣寮曠敤銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, FunctionNodeRef functionNode)
    {
        if (functionNode.MemberKind != MemberKind.Method || !functionNode.IsPrivate)
        {
            yield break;
        }

        if (context.Inheritance.IsOverrideMember(functionNode.MemberId.Value) ||
            context.Inheritance.ImplementsInterfaceMember(functionNode.MemberId.Value))
        {
            yield break;
        }

        if (IsKnownFrameworkEntrypoint(context, functionNode))
        {
            yield break;
        }

        var hasReferences = context.References.HasReferences(functionNode.MemberId.Value);
        if (!hasReferences)
        {
            yield return MarkDecision.ForTarget(
                ToMethodTarget(functionNode),
                PlanActionKind.Delete,
                "function-mark",
                "Method has no references and is not protected by inheritance or interface implementation.");
            yield break;
        }

        if (!functionNode.ReturnsVoid && functionNode.HasBody && !functionNode.HasStatements)
        {
            yield return MarkDecision.ForTarget(
                ToMethodTarget(functionNode),
                PlanActionKind.AddReturn,
                "function-mark",
                "Referenced non-void method has an empty body and requires a default return.",
                payload: DefaultValueFormatter.Format(functionNode.ReturnTypeDisplay));
        }
    }

    private static PlanTarget ToMethodTarget(FunctionNodeRef functionNode) =>
        new(
            functionNode.DocumentPath,
            functionNode.MemberId,
            functionNode.MemberKind,
            TargetKind.Method,
            functionNode.SpanStart,
            functionNode.SpanLength,
            functionNode.DisplayName);

    private static bool IsKnownFrameworkEntrypoint(AnalysisContext context, FunctionNodeRef functionNode)
    {
        var methodName = TryGetMethodName(functionNode.MemberId.Value);
        if (string.IsNullOrEmpty(methodName) || !KnownFrameworkEntrypointNames.Contains(methodName))
        {
            return IsProgramEntrypoint(functionNode, methodName);
        }

        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, functionNode.DeclaringTypeId, StringComparison.Ordinal) &&
            edge.Kind is TypeDependencyKind.Inherits or TypeDependencyKind.Implements &&
            KnownFrameworkTypeMarkers.Any(marker => edge.TargetTypeId.Contains(marker, StringComparison.Ordinal)));
    }

    private static bool IsProgramEntrypoint(FunctionNodeRef functionNode, string methodName)
    {
        if (!string.Equals(methodName, "Main", StringComparison.Ordinal))
        {
            return false;
        }

        return functionNode.ReturnsVoid &&
               (functionNode.MemberId.Value.EndsWith(".Main(string[])", StringComparison.Ordinal) ||
                functionNode.MemberId.Value.EndsWith(".Main()", StringComparison.Ordinal));
    }

    private static string TryGetMethodName(string memberId)
    {
        var parameterStart = memberId.IndexOf('(');
        if (parameterStart <= 0)
        {
            return string.Empty;
        }

        var lastDot = memberId.LastIndexOf('.', parameterStart);
        if (lastDot < 0 || lastDot + 1 >= parameterStart)
        {
            return string.Empty;
        }

        return memberId.Substring(lastDot + 1, parameterStart - lastDot - 1);
    }
}

/// <summary>
/// 绫绘爣璁拌鍒欍€?
/// </summary>
/// <summary>
/// 绫绘爣璁拌鍒欍€?
/// 瑙勫垯鏃忥細IClassRule銆?
/// 鍛戒腑鐩爣锛氭湭琚户鎵裤€佸紩鐢ㄦ垨楂橀闄╃姸鎬佷繚鎶ょ殑 class target銆?
/// 鐩存帴浜у嚭锛歝lass 绾?Delete锛宺eason 涓?class-mark銆?
/// 浼犳挱锛氫笉閫傜敤銆?
/// 渚濇嵁锛欼InheritanceQueryService銆両ReferenceQueryService銆?
/// 闃绘柇锛氶珮椋庨櫓绫诲瀷銆佷綅浜庣户鎵块摼涓€佷粛鏈夊紩鐢ㄣ€佹槑纭殑娉ㄥ唽/缁勫悎鍣ㄦ寔鏈夈€?
/// 鎻愬崌锛氬惁銆?
/// 鏈€灏忔祴璇曪細
/// - Execute_EmitsDeleteForUnreferencedNestedClass
/// - Execute_EmitsDeleteForUnreferencedTopLevelInternalClass
/// - Execute_DoesNotDeleteReferencedClass
/// - Execute_DoesNotDeletePublicTopLevelClass
/// - Execute_DoesNotDeleteClassRegisteredInStaticManager
/// - Execute_DoesNotDeleteClassRegisteredViaGenericRegister
/// - Execute_DoesNotDeleteClassRegisteredViaManagerIndexer
/// - Execute_DoesNotDeleteRuleNodeAddedToComposerChain
/// - Execute_DeletesTypeAddedOnlyToLocalTemporaryList
/// </summary>
public sealed class ClassMarkingRule : IClassRule
{
    /// <summary>
    /// 璇勪及鍒嗘瀽鐩爣骞剁敓鎴愭爣璁板喅绛栥€?
    /// </summary>
    /// <param name="context">鍒嗘瀽涓婁笅鏂囥€?/param>
    /// <param name="target">鍒嗘瀽鐩爣銆?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target)
    {
        var typeId = target.Target.MemberId.Value;
        if (target.Target.TargetKind != TargetKind.Class)
        {
            yield break;
        }

        if (target.IsHighRisk ||
            context.Inheritance.IsInInheritanceChain(typeId) ||
            context.References.HasReferences(typeId) ||
            IsKnownFrameworkType(context, typeId))
        {
            yield break;
        }

        yield return MarkDecision.ForTarget(
            target.Target,
            PlanActionKind.Delete,
            "class-mark",
            "Class has no references and is not protected by inheritance.");
    }

    private static bool IsKnownFrameworkType(AnalysisContext context, string typeId)
    {
        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, typeId, StringComparison.Ordinal) &&
            edge.Kind is TypeDependencyKind.Inherits or TypeDependencyKind.Implements &&
            (edge.TargetTypeId.Contains("GenPass", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenShape", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenAction", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenStructure", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("NetSocialModule", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("ISocialModule", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("IItemDropRule", StringComparison.Ordinal)));
    }
}

/// <summary>
/// 璋冪敤璇彞杈圭晫鎻愬崌瑙勫垯銆?
/// </summary>
/// <summary>
/// 璋冪敤杈圭晫鎻愬崌瑙勫垯銆?
/// 瑙勫垯鏃忥細IBoundaryPromotionRule銆?
/// 鍛戒腑鐩爣锛歞irect statement-level Delete锛屼笖璇?statement 鎭板ソ璋冪敤涓€涓鏈夋柟娉曘€?
/// 鐩存帴浜у嚭锛歮ethod 绾?Delete锛宺eason 涓?boundary-promotion銆?
/// 浼犳挱锛氬惁锛涜瑙勫垯娑堣垂宸叉湁 statement decision锛屾湰韬笉浜х敓浼犳挱銆?
/// 渚濇嵁锛欼nvokedMemberIds銆丗unctionIndex銆両ReferenceQueryService銆両InheritanceQueryService銆?
/// 闃绘柇锛歱ropagated delete銆侀潪绉佹湁鎴栧彈淇濇姢鐨勬柟娉曘€佷粛鏈夊墿浣欏紩鐢ㄣ€佸璋冪敤 statement銆佸凡瀛樺湪 method delete銆?
/// 鎻愬崌锛氭槸锛屽浐瀹氫粠 statement delete 鎻愬崌鍒?method delete銆?
/// 鏈€灏忔祴璇曪細
/// - InvocationBoundaryPromotionRule_PromotesSingleStatementDeleteToMethodDelete
/// - InvocationBoundaryPromotionRule_DoesNotPromotePropagatedDelete
/// - BoundaryPromotionEngine_DoesNotDuplicateExistingMethodDelete
/// </summary>
public sealed class InvocationBoundaryPromotionRule : IBoundaryPromotionRule
{
    /// <summary>
    /// 璇勪及鏄惁杩涜杈圭晫鎻愬崌銆?
    /// </summary>
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target, MarkDecision decision)
    {
        if (target.Target.TargetKind != TargetKind.Statement ||
            decision.Action.Kind != PlanActionKind.Delete ||
            decision.Reason.Origin == DecisionOrigin.Propagation ||
            string.Equals(decision.Reason.RuleId, "dataflow-propagation", StringComparison.Ordinal) ||
            target.InvokedMemberIds.Count != 1)
        {
            yield break;
        }

        var invokedMemberId = target.InvokedMemberIds[0];
        if (!context.FunctionIndex.NodesByMemberId.TryGetValue(invokedMemberId.Value, out var functionNode) ||
            functionNode.MemberKind != MemberKind.Method ||
            !functionNode.IsPrivate ||
            !functionNode.HasBody ||
            context.Inheritance.IsOverrideMember(invokedMemberId.Value) ||
            context.Inheritance.ImplementsInterfaceMember(invokedMemberId.Value))
        {
            yield break;
        }

        var remainingReferences = context.References.GetReferencingFunctions(invokedMemberId.Value)
            .Select(memberId => memberId.Value)
            .ToHashSet(StringComparer.Ordinal);
        remainingReferences.Remove(target.Target.MemberId.Value);
        if (remainingReferences.Count > 0)
        {
            yield break;
        }

        yield return MarkDecision.ForTarget(
            new PlanTarget(
                functionNode.DocumentPath,
                functionNode.MemberId,
                functionNode.MemberKind,
                TargetKind.Method,
                functionNode.SpanStart,
                functionNode.SpanLength,
                functionNode.DisplayName,
                new TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength)),
            PlanActionKind.Delete,
            "boundary-promotion",
            "Invocation delete crossed the statement boundary and was promoted to a method delete candidate.",
            sourceTargetKey: target.Target.TargetKey,
            sourceTargetDisplayText: target.Target.DisplayText,
            sourceMemberId: target.Target.MemberId.Value,
            boundaryKind: BoundaryKind.Invocation,
            triggeredSymbolKeys: new[] { invokedMemberId.Value },
            relatedSymbolKeys: new[] { invokedMemberId.Value },
            relatedSymbolNames: new[] { functionNode.DisplayName },
            origin: DecisionOrigin.BoundaryPromotion);
    }
}

/// <summary>
/// 鐖跺潡绌块€忎綔鐢ㄥ煙閫夋嫨瑙勫垯銆?
/// </summary>
/// <summary>
/// 鐖跺潡绌块€忎綔鐢ㄥ煙瑙勫垯銆?
/// 瑙勫垯鏃忥細IStatementScopeRule銆?
/// 鍛戒腑鐩爣锛氳鍙栦簡鍙傛暟鎴栫埗鍧?local锛屼笖婊¤冻鐖跺潡绌块€忔潯浠剁殑 statement seed銆?
/// 鐩存帴浜у嚭锛氭棤锛涜瑙勫垯鍙礋璐ｆ妸 StatementScopeMode 浠?MinimalBlock 鍒囧埌 ParentBlockPiercing銆?
/// 浼犳挱锛氫笉閫傜敤锛涜瑙勫垯鍙敼鍙?statement snapshot 鐨勫彲瑙佽寖鍥淬€?
/// 渚濇嵁锛歋tatementFactsIndex銆乁sesSymbols銆丼copeId銆丳arentScopeId銆?
/// 闃绘柇锛氶珮椋庨櫓 target銆佸璞″垵濮嬪寲璧嬪€笺€乻anitizing assignment銆佺己澶?scope 淇℃伅銆佽法 function boundary銆?
/// 鎻愬崌锛氬惁銆?
/// 鏈€灏忔祴璇曪細
/// - ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired
/// - Execute_DoesNotPropagateAcrossParentBlockByDefault
/// - Execute_UsesExplicitStatementScopeModeFromExecutionContext
/// </summary>
public sealed class ParentBlockPiercingScopeRule : IStatementScopeRule
{
    /// <summary>
    /// 閫夋嫨璇彞浣滅敤鍩熸ā寮忋€?
    /// </summary>
    public StatementScopeMode SelectScopeMode(AnalysisContext context, AnalysisTarget seedTarget)
    {
        if (seedTarget.Target.TargetKind != TargetKind.Statement ||
            seedTarget.IsHighRisk ||
            seedTarget.IsObjectInitializerAssignment ||
            seedTarget.IsSanitizingAssignment ||
            string.IsNullOrEmpty(seedTarget.ScopeId) ||
            string.IsNullOrEmpty(seedTarget.ParentScopeId) ||
            !context.StatementFacts.FactsByMemberId.TryGetValue(seedTarget.Target.MemberId.Value, out var bucket))
        {
            return StatementScopeMode.MinimalBlock;
        }

        var sameScopeDefinitions = bucket
            .Where(fact =>
                string.Equals(fact.ScopeId, seedTarget.ScopeId, StringComparison.Ordinal) &&
                fact.SpanStart < seedTarget.Target.SpanStart)
            .SelectMany(fact => fact.DefinesSymbols)
            .Select(symbol => symbol.SymbolKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in seedTarget.UsesSymbols)
        {
            if (symbol.DeclaringMemberId != seedTarget.Target.MemberId)
            {
                continue;
            }

            if (symbol.SymbolKind == SymbolKindRef.Parameter)
            {
                return StatementScopeMode.ParentBlockPiercing;
            }

            if (symbol.SymbolKind == SymbolKindRef.Local && !sameScopeDefinitions.Contains(symbol.SymbolKey))
            {
                return StatementScopeMode.ParentBlockPiercing;
            }
        }

        return StatementScopeMode.MinimalBlock;
    }
}

/// <summary>
/// 鏍囪瑙勫垯娉ㄥ唽琛ㄣ€?
/// </summary>
public sealed class MarkingRuleRegistry
{
    /// <summary>
    /// 鍒濆鍖栨爣璁拌鍒欐敞鍐岃〃鐨勬柊瀹炰緥銆?
    /// </summary>
    /// <param name="seedRules">绉嶅瓙瑙勫垯銆?/param>
    /// <param name="expressionProjectionRules">琛ㄨ揪寮忔姇褰辫鍒欍€?/param>
    /// <param name="propagationRules">浼犳挱瑙勫垯銆?/param>
    /// <param name="protectionRules">淇濇姢瑙勫垯銆?/param>
    /// <param name="methodRules">鏂规硶瑙勫垯銆?/param>
    /// <param name="classRules">绫昏鍒欍€?/param>
    /// <param name="boundaryPromotionRules">杈圭晫鎻愬崌瑙勫垯銆?/param>
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

    /// <summary>
    /// 鑾峰彇绉嶅瓙瑙勫垯銆?
    /// </summary>
    public IReadOnlyList<ISeedRule> SeedRules { get; }

    /// <summary>
    /// 鑾峰彇琛ㄨ揪寮忔姇褰辫鍒欍€?
    /// </summary>
    public IReadOnlyList<IExpressionProjectionRule> ExpressionProjectionRules { get; }

    /// <summary>
    /// 鑾峰彇浼犳挱瑙勫垯銆?
    /// </summary>
    public IReadOnlyList<IPropagationRule> PropagationRules { get; }

    /// <summary>
    /// 鑾峰彇淇濇姢瑙勫垯銆?
    /// </summary>
    public IReadOnlyList<IProtectionRule> ProtectionRules { get; }

    /// <summary>
    /// 鑾峰彇鏂规硶瑙勫垯銆?
    /// </summary>
    public IReadOnlyList<IMethodRule> MethodRules { get; }

    /// <summary>
    /// 鑾峰彇鎴愬憳 target 瑙勫垯銆?
    /// </summary>
    public IReadOnlyList<IMemberTargetRule> MemberTargetRules { get; }

    /// <summary>
    /// 鑾峰彇绫昏鍒欍€?
    /// </summary>
    public IReadOnlyList<IClassRule> ClassRules { get; }

    /// <summary>
    /// 鑾峰彇杈圭晫鎻愬崌瑙勫垯銆?
    /// </summary>
    public IReadOnlyList<IBoundaryPromotionRule> BoundaryPromotionRules { get; }

    /// <summary>
    /// 鑾峰彇璇彞浣滅敤鍩熻鍒欍€?
    /// </summary>
    public IReadOnlyList<IStatementScopeRule> StatementScopeRules { get; }

    /// <summary>
    /// 鍒涘缓榛樿瑙勫垯娉ㄥ唽琛ㄣ€?
    /// </summary>
    /// <returns>榛樿瑙勫垯娉ㄥ唽琛ㄣ€?/returns>
    public static MarkingRuleRegistry CreateDefault() =>
        new(
            [new DirectiveSeedRule()],
            [new ExpressionProjectionRule()],
            [new SanitizationPropagationRule()],
            [new HighRiskProtectionRule(), new ObjectInitializerProtectionRule()],
            [new FunctionMarkingRule(), new PublicMethodPrivatizationRule(), new UnusedMethodRule()],
            [new UnusedMemberRule()],
            [new UnusedClassRule(), new PublicMethodOrderingRule()],
            [new InvocationBoundaryPromotionRule()],
            [new ParentBlockPiercingScopeRule()]);
}

/// <summary>
/// 鏍囪瑙勫垯寮曟搸銆?
/// </summary>
public sealed class MarkingRuleEngine
{
    private readonly MarkingRuleRegistry _registry;
    private readonly StatementPropagationEngine _statementPropagationEngine;
    private readonly BoundaryPromotionEngine _boundaryPromotionEngine;

    /// <summary>
    /// 鍒濆鍖栨爣璁拌鍒欏紩鎿庣殑鏂板疄渚嬨€?
    /// </summary>
    /// <param name="registry">鏍囪瑙勫垯娉ㄥ唽琛ㄣ€?/param>
    public MarkingRuleEngine(
        MarkingRuleRegistry registry,
        StatementPropagationEngine? statementPropagationEngine = null,
        BoundaryPromotionEngine? boundaryPromotionEngine = null)
    {
        _registry = registry;
        _statementPropagationEngine = statementPropagationEngine ?? new StatementPropagationEngine(registry);
        _boundaryPromotionEngine = boundaryPromotionEngine ?? new BoundaryPromotionEngine(registry);
    }

    public IReadOnlyList<MarkDecision> Execute(AnalysisEngineResult analysisResult) =>
        Execute(
            analysisResult.Snapshot,
            analysisResult.Services,
            new RuleExecutionContext(
                "MarkingRuleEngine",
                null,
                StatementScopeMode.MinimalBlock,
                CancellationToken.None,
                "AnalysisEngineResult execution"));

    public IReadOnlyList<MarkDecision> Execute(
        AnalysisExecutionSnapshot snapshot,
        AnalysisServices services,
        RuleExecutionContext executionContext)
    {
        return ExecuteCore(
            AnalysisContext.Create(snapshot, services),
            executionContext,
            includeMethodRules: true);
    }

    /// <summary>
    /// 鎵ц鍒嗘瀽涓婁笅鏂囩殑鏍囪瑙勫垯銆?
    /// </summary>
    /// <param name="context">鍒嗘瀽涓婁笅鏂囥€?/param>
    /// <returns>鏍囪鍐崇瓥闆嗗悎銆?/returns>
    public IReadOnlyList<MarkDecision> Execute(AnalysisContext context)
    {
        return ExecuteCore(
            context,
            new RuleExecutionContext(
                "MarkingRuleEngine",
                null,
                StatementScopeMode.MinimalBlock,
                CancellationToken.None,
                "AnalysisContext execution"),
            includeMethodRules: true);
    }

    private IReadOnlyList<MarkDecision> ExecuteCore(
        AnalysisContext context,
        RuleExecutionContext executionContext,
        bool includeMethodRules)
    {
        var seedDecisionsByTarget = new Dictionary<string, List<MarkDecision>>(StringComparer.Ordinal);
        var targetsByKey = context.View.Targets.ToDictionary(target => target.Target.TargetKey, StringComparer.Ordinal);

        foreach (var target in context.View.Targets)
        {
            if (executionContext.SeedTarget != null &&
                target.Target.TargetKey != executionContext.SeedTarget.TargetKey)
            {
                continue;
            }

            if (IsProtected(target))
            {
                continue;
            }

            foreach (var rule in _registry.SeedRules)
            {
                foreach (var decision in rule.Evaluate(target))
                {
                    if (!seedDecisionsByTarget.TryGetValue(decision.Target.TargetKey, out var list))
                    {
                        list = new List<MarkDecision>();
                        seedDecisionsByTarget[decision.Target.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }

            foreach (var rule in _registry.ExpressionProjectionRules)
            {
                foreach (var decision in rule.Evaluate(target))
                {
                    if (!seedDecisionsByTarget.TryGetValue(decision.Target.TargetKey, out var list))
                    {
                        list = new List<MarkDecision>();
                        seedDecisionsByTarget[decision.Target.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }
        }

        // Propagation only reads direct seed decisions; build the read-only view once
        // for the whole execution to avoid per-seed map copies.
        var seedDecisionView = seedDecisionsByTarget.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<MarkDecision>)pair.Value,
            StringComparer.Ordinal);

        var finalDecisions = seedDecisionsByTarget.Values.SelectMany(list => list).ToList();
        foreach (var seedGroup in seedDecisionsByTarget)
        {
            if (!targetsByKey.TryGetValue(seedGroup.Key, out var seedTarget) ||
                seedTarget.Target.TargetKind != TargetKind.Statement ||
                IsProtected(seedTarget))
            {
                continue;
            }

            finalDecisions.AddRange(
                _statementPropagationEngine.Propagate(
                    context,
                    executionContext,
                    seedTarget,
                    seedDecisionView));
        }

        finalDecisions.AddRange(_boundaryPromotionEngine.Promote(context, finalDecisions, targetsByKey));

        if (includeMethodRules)
        {
            foreach (var functionNode in context.FunctionIndex.NodesByMemberId.Values.OrderBy(node => node.MemberId.Value, StringComparer.Ordinal))
            {
                foreach (var rule in _registry.MethodRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, functionNode));
                }
            }

            foreach (var target in context.View.Targets.Where(target => target.Target.TargetKind is TargetKind.Field or TargetKind.Property))
            {
                foreach (var rule in _registry.MemberTargetRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, target));
                }
            }

            foreach (var target in context.View.Targets.Where(target => target.Target.TargetKind == TargetKind.Class))
            {
                foreach (var rule in _registry.ClassRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, target));
                }
            }
        }

        return finalDecisions
            .GroupBy(decision => $"{decision.Target.TargetKey}|{decision.Action.Kind}|{decision.Reason.RuleId}|{decision.Reason.SourceTargetKey}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private bool IsProtected(AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));

}

/// <summary>
/// 榛樿鍊兼牸寮忓寲鍣ㄣ€?
/// </summary>
internal static class DefaultValueFormatter
{
    /// <summary>
    /// 鏍煎紡鍖栭粯璁ゅ€笺€?
    /// </summary>
    /// <param name="returnTypeDisplay">杩斿洖绫诲瀷鏄剧ず鍚嶇О銆?/param>
    /// <returns>鏍煎紡鍖栧悗鐨勯粯璁ゅ€煎瓧绗︿覆銆?/returns>
    public static string Format(string returnTypeDisplay) =>
        returnTypeDisplay switch
        {
            "bool" => "false",
            "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "decimal" or "float" or "double" => "0",
            "char" => "'\\0'",
            "void" => string.Empty,
            var value when value.EndsWith("?", StringComparison.Ordinal) => "null",
            _ when IsReferenceTypeLike(returnTypeDisplay) => "null",
            _ => "default"
        };

    private static bool IsReferenceTypeLike(string returnTypeDisplay)
    {
        if (string.Equals(returnTypeDisplay, "string", StringComparison.Ordinal))
        {
            return true;
        }

        return returnTypeDisplay.Contains('.', StringComparison.Ordinal) ||
               returnTypeDisplay.Contains('<', StringComparison.Ordinal);
    }
}

