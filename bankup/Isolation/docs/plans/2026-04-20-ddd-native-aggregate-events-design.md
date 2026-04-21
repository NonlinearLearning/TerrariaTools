# 2026-04-20 DDD native aggregate behavior uplift

## Context

Current Domain objects already model the workflow chain, but the repo review identified three gaps:

1. `RewriteDecision` still relies on `Logic/Decision/RewriteDecisionMaker` for its main approval/rejection policy.
2. domain events are mostly synthesized in Logic sequence builders instead of being recorded by aggregates when business facts occur.
3. base entities/aggregates expose identity only, so richer aggregate behavior cannot follow the ABP / MyMeetings style (`CheckRule` + `AddDomainEvent`) without ad-hoc duplication.

## Chosen slice

Apply a bounded uplift instead of a repo-wide rewrite:

- add native domain-event recording support to `Entity` / `AggregateRoot`
- move candidate decision policy into `Domain.Decision.RewriteDecision`
- let `RewritePlan` and `VerificationEvidence` emit native business events when meaningful transitions occur
- teach workflow event assembly to reuse native aggregate events first and synthesize fallbacks only when needed

## Why this slice

This is the smallest change that directly improves all four low-scoring areas:

- **aggregate design**: the decision aggregate becomes the consistency and policy center
- **rich domain model**: approval/rejection/conflict/protection policy moves from Logic into Domain behavior
- **domain events**: aggregates naturally record their own events
- **external DDD alignment**: closer to ABP / modular-monolith-with-ddd style without rewriting unrelated bounded contexts

## Non-goals

- no repository-wide aggregate rewrite
- no change to four-layer dependency direction
- no new packages
- no removal of existing stage/event builders; they remain compatibility fallbacks

## Test-first plan

1. add failing tests proving `RewriteDecision` can resolve approval/rejection scenarios by itself and records a native `DecisionCompletedDomainEvent`
2. add failing tests proving `RewritePlan` / `VerificationEvidence` expose native domain events for compile/evidence milestones
3. implement minimal production changes until those tests pass
4. run serial build + analysis tests + architecture smoke

## Risk controls

- keep event correlation deterministic by allowing aggregates to record events with explicit correlation id; fallback to aggregate id when omitted
- preserve current public APIs by making Logic use new aggregate methods instead of deleting orchestrators
- verify workflow event builder still deduplicates upstream events

## 文档同步与实现约束（2026-04 全量升级）

### 文档类型

本文属于：执行计划文档。

### 代码对齐文档要求

- 影响本文覆盖范围的代码变更，默认同批更新本文，或在同一任务链路说明无需更新的理由。
- 本文中的路径、类型、方法、流程、默认值、已知问题和验收口径失效时，必须同步修正。
- 关键结论优先绑定真实代码、真实测试、真实计划和真实日志。

### 文档对齐代码要求

- 实现本文覆盖范围内的代码前，先读取 `docs/约束/代码对齐文档约束.md` 与 `docs/约束/文档对齐代码约束.md`。
- 代码与本文冲突时，当轮完成“改代码”或“改文档”的闭环。
- 稳定规则优先继续下沉到测试、ArchitectureTests、构建检查或流程守护。

### 默认代码锚点

- `对应 src/** 实现文件`
- `对应 tests/** 回归测试`
- `对应 log/*.log 验证日志`
- `.omx/plans/*.md`

### 交付检查

- 本文与当前代码事实一致；
- 本文与当前测试、计划、日志不冲突；
- 本文涉及的关键约束具备可追踪验证锚点。
