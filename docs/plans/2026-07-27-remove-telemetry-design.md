# Remove Telemetry Design

## Goal

Remove telemetry collection, telemetry result models, telemetry output, and
telemetry-only verification from the NLCPG and NLISSN code paths.

## Scope

- Remove `*Telemetry` types, counters, timers, snapshots, cache statistics, and
  result properties used only to expose them.
- Remove the NLISSN telemetry writers and their command-host integration.
- Remove CPG persistence benchmark telemetry output and telemetry-only tests.
- Preserve functional graph construction, persistence, querying, analysis,
  diagnostics, rewrite artifacts, and exceptions.

## Approach

Delete the output and consumer layers first, then remove result-model fields,
then remove collection code. Replace telemetry assertions with functional
equivalence assertions where they protect behavior. Do not retain no-op
compatibility types or zero-value fields.

## Risks

The current worktree contains an uncommitted MinimalRoslynCpg-to-NLCPG
migration. Changes must be limited to telemetry dependencies and must not
revert or overwrite migration work.
