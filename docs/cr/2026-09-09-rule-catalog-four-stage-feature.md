# Rule Catalog Four-Stage Feature Change

## Change Record

- Change ID: `CR-2026-09-09-rule-catalog-four-stage-feature`
- Requested through: current task message
- Requested date: 2026-09-09
- Level: major
- Decision: accepted for the current rule-catalog execution

## Original Requirement

> 强制所有 feature 四阶段完整。
> 需要先定义并实现这两个 feature 的真实 Propagate/Lift 规则，包括它们的输入、输出、规则图节点和行为等价基线；不能用 no-op 占位规则代替真实规则。

## Impact

- The generated catalog, stage-completeness diagnostics, rule graph nodes and edges, and Composer baselines change.
- The current generated catalog has no feature dimension; registration is a flat inclusion marker.
- The four named deferred rule families are excluded from the current generated catalog and have no Composer compatibility path.
- The hand-written `RuleRegistry` is removed after the generated catalog is connected.

## Acceptance Scope

- The current catalog has Mark, Propagate, Lift, and Propose rules.
- Missing current-catalog stages produce generator error `NLRCG011` and no partial catalog.
- The four named deferred rule families are absent from generated descriptors, factories, graph declarations, and Composer compatibility paths.
- Core Contract and Host checks pass.

## Superseding Scope Change

Requested through the current task message on 2026-09-09:

> UnreachableMethodDeletion / UnreferencedMethodDeletion这种feature不进入当前的实现

This scope change supersedes the earlier four-feature implementation request for the current implementation. All four named rule families remain outside the current framework; their rules, descriptors, fact kinds, payloads, graph nodes, compatibility paths, and behavior-equivalence baselines are excluded. The four-stage completeness rule remains in force for the flat current catalog.
