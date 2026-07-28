namespace NLISSN.Rules;

// 默认规则的依赖清单。它按稳定 RuleId 声明数据边，不从 GroupKey 推导调度关系。
public static class RuleGraphDependencyCatalog
{
    private static readonly string[] SObjectMarkerIds =
    {
        "DEL-SOBJ-MARK-ID-001", "DEL-SOBJ-MARK-THIS-001", "DEL-SOBJ-MARK-BASE-001",
        "DEL-SOBJ-MARK-DECL-001", "DEL-SOBJ-MARK-LIT-NUM-001", "DEL-SOBJ-MARK-LIT-STR-001",
        "DEL-SOBJ-MARK-LIT-TRUE-001", "DEL-SOBJ-MARK-LIT-FALSE-001", "DEL-SOBJ-MARK-LIT-NULL-001",
        "DEL-SOBJ-MARK-MEMBER-001", "DEL-SOBJ-MARK-BINDING-001", "DEL-SOBJ-MARK-INVOKE-001",
        "DEL-SOBJ-MARK-NEW-001", "DEL-SOBJ-MARK-IMPLICIT-NEW-001", "DEL-SOBJ-MARK-ELEMENT-001",
        "DEL-SOBJ-MARK-CONDITIONAL-001"
    };

    private static readonly string[] SObjectPropagatorIds =
    {
        "DEL-SOBJ-PROP-ASSIGN-LHS-001", "DEL-SOBJ-PROP-DECL-INIT-001",
        "DEL-SOBJ-PROP-LOGIC-001", "DEL-SOBJ-PROP-LOGIC-GROUP-001",
        "DEL-SOBJ-PROP-IF-COMPLETE-001", "DEL-SOBJ-PROP-SYMBOL-001"
    };

    private static readonly string[] SObjectLifterIds =
    {
        "DEL-SOBJ-LIFT-HOST-001", "DEL-SOBJ-LIFT-IF-001", "DEL-SOBJ-LIFT-SWITCH-001"
    };

    private static readonly string[] ClassMarkerIds =
    {
        "DEL-CLASS-MARK-DECL-001", "DEL-CLASS-MARK-EXPR-001", "DEL-CLASS-MARK-TYPE-001"
    };

    private static readonly string[] ClassPropagatorIds =
    {
        "DEL-CLASS-PROP-DECL-HOST-001", "DEL-CLASS-PROP-DELEGATE-USAGE-001",
        "DEL-CLASS-PROP-EXT-MAPPED-001", "DEL-CLASS-PROP-IF-COMPLETE-001",
        "DEL-CLASS-PROP-INDEXER-PARAM-USAGE-001", "DEL-CLASS-PROP-LOCALFUNC-PARAM-USAGE-001",
        "DEL-CLASS-PROP-METHOD-PARAM-USAGE-001", "DEL-CLASS-PROP-NEW-DECL-001",
        "DEL-CLASS-PROP-LOCAL-REF-001"
    };

    private static readonly string[] ClassLifterIds =
    {
        "DEL-CLASS-LIFT-HOST-001", "DEL-CLASS-LIFT-IF-001", "DEL-CLASS-LIFT-SWITCH-001"
    };

    public static IReadOnlyList<RuleDependency> GetDependencies(
      IRuleDefinition rule,
      RuleKind kind,
      IReadOnlyList<RuleDependency> declaredDependencies)
    {
        var dependencies = declaredDependencies.ToList();
        if (kind == RuleKind.Lift && rule.RuleId.EndsWith("-LIFT-SWITCH-001", StringComparison.Ordinal))
        {
            // Switch 只消费 host / if lift facts；不再等待整个规则家族。
        }
        else if (kind == RuleKind.Lift && rule.RuleId.StartsWith("DEL-SOBJ-LIFT-", StringComparison.Ordinal))
        {
            AddUnique(dependencies, FamilyFacts(SObjectMarkerIds, SObjectPropagatorIds));
        }
        else if (kind == RuleKind.Lift && rule.RuleId.StartsWith("DEL-CLASS-LIFT-", StringComparison.Ordinal))
        {
            AddUnique(dependencies, FamilyFacts(ClassMarkerIds, ClassPropagatorIds));
        }
        else if (kind == RuleKind.Propose)
        {
            AddUnique(dependencies, GetProposalDependencies(rule.RuleId));
            if (TryGetStandaloneMarker(rule.RuleId, out var markerId))
            {
                AddUnique(dependencies, new[]
                {
                    new RuleDependency(RuleNodeId.For(RuleKind.Mark, markerId), RuleOutputKind.SeedMark)
                });
            }
        }

        return dependencies;
    }

    private static IReadOnlyList<RuleDependency> FamilyFacts(
      IReadOnlyList<string> markerIds,
      IReadOnlyList<string> propagatorIds,
      IReadOnlyList<string>? lifterIds = null)
    {
        var dependencies = markerIds
          .Select(id => new RuleDependency(RuleNodeId.For(RuleKind.Mark, id), RuleOutputKind.SeedMark))
          .Concat(propagatorIds.Select(id => new RuleDependency(RuleNodeId.For(RuleKind.Propagate, id), RuleOutputKind.PropagatedMark)));
        if (lifterIds is not null)
        {
            dependencies = dependencies.Concat(lifterIds.Select(id => new RuleDependency(RuleNodeId.For(RuleKind.Lift, id), RuleOutputKind.LiftedMark)));
        }

        return dependencies.ToList();
    }

    private static IReadOnlyList<RuleDependency> GetProposalDependencies(string ruleId)
    {
        return ruleId switch
        {
            // 这两条规则必须查看所有事实，才能正确判断哪些 mark 已被专门规则接管。
            "DEL-SOBJ-PROPOSE-DEFAULT-001" or "DEL-SOBJ-PROPOSE-CTRL-001" =>
              FamilyFacts(SObjectMarkerIds, SObjectPropagatorIds, SObjectLifterIds),
            "DEL-SOBJ-PROPOSE-LOGIC-001" =>
              OutputOf(RuleKind.Propagate, "DEL-SOBJ-PROP-LOGIC-GROUP-001", RuleOutputKind.PropagatedMark),
            "DEL-SOBJ-PROPOSE-IF-001" =>
              OutputOf(RuleKind.Propagate, "DEL-SOBJ-PROP-IF-COMPLETE-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-DEFAULT-001" or "DEL-CLASS-PROP-CTRL-001" =>
              FamilyFacts(ClassMarkerIds, ClassPropagatorIds, ClassLifterIds),
            "DEL-CLASS-PROP-IF-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-IF-COMPLETE-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-TYPE-DECL-001" or
            "DEL-CLASS-PROP-RETURN-001" or
            "DEL-CLASS-PROP-PUBLIC-RETURN-001" or
            "DEL-CLASS-PROP-IFACE-METHOD-001" or
            "DEL-CLASS-PROP-IFACE-PROPERTY-001" or
            "DEL-CLASS-PROP-IFACE-EVENT-001" or
            "DEL-CLASS-PROP-IFACE-INDEXER-001" or
            "DEL-CLASS-PROP-DELEGATE-001" or
            "DEL-CLASS-PROP-EXT-RECV-001" or
            "DEL-CLASS-PROP-BASE-001" or
            "DEL-CLASS-PROP-GENERIC-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-DECL-HOST-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-PRIVATE-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-NAMED-METHOD-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-OPTIONAL-METHOD-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-PARAMS-METHOD-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-PUBLIC-PARAM-SHRINK-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-METHOD-PARAM-USAGE-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-NAMED-LOCALFUNC-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-OPTIONAL-LOCALFUNC-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-LOCALFUNC-PARAM-SHRINK-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-LOCALFUNC-PARAM-USAGE-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-NAMED-INDEXER-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-INDEXER-PARAM-SHRINK-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-INDEXER-PARAM-USAGE-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-DELEGATE-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-METHODGROUP-DELEGATE-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-LAMBDA-DELEGATE-PARAM-SHRINK-001" or
            "DEL-CLASS-PROP-DELEGATE-INVOKE-PARAM-SHRINK-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-DELEGATE-USAGE-001", RuleOutputKind.PropagatedMark),

            "DEL-CLASS-PROP-EXT-NONRECV-PARAM-SHRINK-001" =>
              OutputOf(RuleKind.Propagate, "DEL-CLASS-PROP-EXT-MAPPED-001", RuleOutputKind.PropagatedMark),
            _ => Array.Empty<RuleDependency>()
        };
    }

    private static IReadOnlyList<RuleDependency> OutputOf(
      RuleKind kind,
      string ruleId,
      RuleOutputKind outputKind)
    {
        return new[] { new RuleDependency(RuleNodeId.For(kind, ruleId), outputKind) };
    }

    private static void AddUnique(List<RuleDependency> target, IReadOnlyList<RuleDependency> additions)
    {
        foreach (var dependency in additions)
        {
            if (target.All(existing => existing.Producer != dependency.Producer))
            {
                target.Add(dependency);
            }
        }
    }

    private static bool TryGetStandaloneMarker(string proposalId, out string markerId)
    {
        markerId = proposalId switch
        {
            "DEL-DEAD-001" => "DEL-DEAD-001",
            "DEL-UNREF-METHOD-PROP-001" => "DEL-UNREF-METHOD-MARK-001",
            "CLR-UNUSED-IFACE-IMPL-PROP-001" => "CLR-UNUSED-IFACE-IMPL-MARK-001",
            "PRIV-INTERNAL-PUBLIC-PROP-001" => "PRIV-INTERNAL-PUBLIC-MARK-001",
            _ => string.Empty
        };
        return markerId.Length > 0;
    }
}
