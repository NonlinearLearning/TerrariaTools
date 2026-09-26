# Unified YAML CLI Configuration Design

**Status:** Approved for implementation on 2026-09-22

## Goal

Move every business control currently accepted by the `NLISSN`, `NLCPG`, and
`NLCPG.ProjectExport` executable entrypoints into one strict `nlissn.yml`
configuration document.

## Entry Contract

All executable entrypoints read `nlissn.yml` from the current working directory
and reject every command-line argument. A required `tool` discriminator prevents
one executable from silently interpreting a configuration intended for another:

```yaml
schemaVersion: 3
tool: nlissn
```

Supported tool values are `nlissn`, `nlcpg`, and `nlcpg-project-export`.

The existing NLISSN configuration remains structurally recognizable. Its
`input`, `analysis`, `execution`, `artifacts`, and `logging` sections remain the
NLISSN branch. The other two tools add only their own branch-specific section.

## Unified Field Mapping

### Common input

`input.path` replaces the NLCPG positional source path and the ProjectExport
`--project` option. Existing workspace options remain shared:

- `targetFramework`
- `configuration`
- `platform`
- `restore`
- `generatedSources`
- `project` where the workspace needs a solution project selector or owning
  project for a single document

### NLCPG

```yaml
nlcpg:
  view:
    mode: stats | local
    anchor:
      nodeId: 42
      fullName: Demo.Sample.Add
      name: Add
    hops: 1
    direction: both | incoming | outgoing
    edgeKinds: []
  output:
    json: ./Build/local-view.json
```

Exactly one anchor selector is required for `local`; `stats` does not require an
anchor. `hops` is non-negative. `edgeKinds` is a YAML sequence of enum names,
replacing the old comma-separated CLI value.

### ProjectExport

```yaml
projectExport:
  output: ./Build/NLCPG-json
  projectWorkerCount: 12
  resume: false
```

`projectWorkerCount` maps to the existing project-pool worker setting and keeps
the effective cap of 12. The existing CLI defaults are preserved. The internal
memory budget options are not CLI parameters and remain programmatic defaults in
this change.

## Loader Boundaries

NLISSN's domain-aware loader continues to own NLISSN artifact preparation,
workspace validation, replay constraints, and resolved configuration output.
NLCPG and ProjectExport use local YAML adapters because the current NLISSN
configuration project depends on NLISSN.Core, which in turn references NLCPG;
adding a reverse reference would create a project dependency cycle. The local
adapters share the Schema 3 field contract and path semantics without importing
NLISSN domain configuration types.

All loaders use duplicate-key checking, reject unknown properties, validate the
tool discriminator, resolve relative paths from the configuration directory,
and return diagnostics before constructing runtime options.

## Compatibility

Schema 2 remains readable for existing NLISSN test fixtures where practical;
the repository example and all new unified tool configurations use Schema 3.
The old executable argument contracts are removed. Programmatic APIs such as
`ProjectExportOptions` remain available to tests and library callers.

## Failure Semantics

- Missing `nlissn.yml` fails before execution.
- Any command-line argument fails before configuration loading.
- A mismatched `tool` fails closed.
- Invalid paths, enums, anchors, duplicate keys, unknown fields, and non-positive
  worker values fail before constructing an exporter, builder, or analysis run.
- No output directory or source rewrite is started after configuration failure.

## Verification

Focused contract tests will cover each tool branch, defaults, all former CLI
fields, argument rejection, tool mismatch, unknown properties, duplicate keys,
invalid anchors, and relative path resolution. Owning project builds and
argument-free CLI smoke tests will run after each implementation batch.
