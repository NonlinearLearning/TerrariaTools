# CR-001: NLCPG Node/Edge Value Model

Status: scheduled
Date: 2026-09-20 (Asia/Shanghai)
Requester: user
Branch: `experiment/domain-model-struct`

## Requirement

> 允许破坏 NLCPGNode 和 NLCPGEdge 的公开 API,移除string? Text = null执行

## Classification

Major. The change alters public type representation and copying/equality semantics in
the graph domain. It can affect nullable behavior, collection keys, boxing, identity
lookups, persistence adapters, and downstream NLISSN decision consumers.

## Scope

- Redesign `NLCPGNode` and `NLCPGEdge` as compact readonly value models.
- Remove `NLCPGNode.Text`.
- Use `NodeId`, stable anchors, or graph ordinals for identity maps and indexes instead
  of reference identity.
- Preserve source-based display fallback, decision reasons/evidence, frozen shard
  schema, graph snapshots, query results, and DOP determinism.

## Non-goals

- No database schema migration.
- No mechanical conversion of graph owners, query state, Roslyn work state, or public
  persistence DTOs unless required to compile the new node/edge boundary.
- No claim that the change alone halves memory or improves all access paths; that
  remains a measurement result.

## Acceptance Evidence

- Contract tests prove the new node/edge type shape and absence of `Text`.
- CPG construction, queries, shard export/restore, decision evidence, and existing
  graph snapshot tests pass.
- DOP equivalence remains valid for the repository's supported settings.
- A build and focused test results record the verified boundaries and any unverified
  performance assumptions.
