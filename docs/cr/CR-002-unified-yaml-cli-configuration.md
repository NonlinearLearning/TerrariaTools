# CR-002: Unify CLI Controls in `nlissn.yml`

Status: scheduled
Date: 2026-09-22 (Asia/Shanghai)
Requester: user
Classification: Major

## Requirement

> 将所有的cli控制参数全部改为配置文件

Follow-up scope confirmation:

> 全部

Follow-up configuration decision:

> 合并为nlissn.yml

## Impact Assessment

- Changes the production entry contract of `NLCPG` and `NLCPG.ProjectExport` from
  argument-driven execution to fixed-working-directory YAML execution.
- Extends the existing NLISSN configuration contract from Schema 2 to a unified
  Schema 3 with a `tool` discriminator.
- Removes the ability to override project input, output, view, anchor, query,
  restore, generated-source, resume, and worker settings from command-line
  arguments.
- Does not change the programmatic `ProjectExportOptions` or graph builder APIs
  used by library callers and tests unless an adapter requires it.
- Requires synchronized updates to schemas, examples, CLI documentation, smoke
  tests, and harness consistency checks.
- No database or persistence schema change is required.

## Approved Design

All three executable entrypoints read `nlissn.yml` from the current working
directory:

| Executable | Required `tool` value |
| --- | --- |
| `NLISSN` | `nlissn` |
| `NLCPG` | `nlcpg` |
| `NLCPG.ProjectExport` | `nlcpg-project-export` |

The shared document uses `schemaVersion: 3`. `input` contains common source and
workspace fields. Existing NLISSN analysis, execution, artifact, and logging
fields remain available for `tool: nlissn`. `tool: nlcpg` adds view, anchor,
traversal, edge-kind, and JSON-output settings. `tool: nlcpg-project-export`
adds output, resume, and project worker settings.

Each executable validates that the selected tool matches its own entrypoint.
Unknown fields, duplicate keys, invalid enum values, invalid paths, invalid
anchor combinations, and invalid numeric values fail before execution.

Relative paths resolve against the directory containing `nlissn.yml`. Executable
entrypoints accept no arguments, including `--help`; help and examples move to
the documentation and Schema contract.

The current project export `--max-degree-of-parallelism` setting becomes the
semantic YAML field `projectExport.projectWorkerCount`. Its existing effective
worker cap of 12 remains unchanged. The NLCPG `--edge-kinds` CSV becomes a YAML
sequence.

To avoid introducing a dependency cycle from NLCPG into the NLISSN domain
configuration assembly, NLCPG and ProjectExport use local adapters for the
unified document while sharing the same schema, field names, path rules, and
tool discriminator. NLISSN keeps its existing domain-aware loader and maps the
new Schema 3 document to the existing runtime settings.

## Acceptance Criteria

- The repository `Miscellaneous/nlissn.yml` loads as `tool: nlissn` with all six
  named concurrency limits intact.
- `NLCPG` runs from a `tool: nlcpg` `nlissn.yml` and supports stats and local
  view operations without CLI arguments.
- `NLCPG.ProjectExport` runs from a `tool: nlcpg-project-export` `nlissn.yml`
  and maps every former business CLI option.
- Passing any argument to any of the three executable entrypoints fails before
  analysis or export begins.
- A tool mismatch fails closed with a diagnostic identifying the expected tool.
- Schema and runtime reject unknown properties, duplicate keys, invalid enum
  values, invalid anchors, and invalid concurrency/worker values.
- Existing programmatic CPG, project export, and NLISSN focused tests preserve
  their behavior.
- CLI reference, quick start, schemas README, examples, and harness checks
  describe only the unified YAML entry contract.

## Verification Boundary

The implementation will run focused configuration/CLI contract tests, owning
project builds, isolated NLCPG and ProjectExport smoke runs, schema checks,
`check-harness-consistency.ps1`, and `git diff --check`. Full long-running
Version4 analysis remains outside this change unless explicitly requested.
