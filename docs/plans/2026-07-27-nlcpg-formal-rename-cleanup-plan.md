# NLCPG formal rename cleanup plan

## Scope

Rename the existing CPG project to `NLCPG` without changing graph construction,
query, persistence, or CLI behavior.

## Steps

1. Rename the project directory and project file to `src/NLCPG/NLCPG.csproj`.
2. Replace the retired project namespace and project references with `NLCPG`.
3. Replace the retired CPG code and file-name prefix with `NLCPG`, including CLI and
   test class/file names.
4. Update current documentation and project references, then confirm no old names remain
   outside historical archives.

## Acceptance

- The NLCPG project and its direct production consumers build.
- Focused CPG contracts compile and execute without name-resolution failures.
- Current source, project, and reader documentation contain no retired project or
  CPG-prefix identifiers.

## Non-goals

- No graph, query, persistence, CLI-option, or runtime-behavior changes.
- Historical progress records are not rewritten.

## Verification record

- `NLCPG.csproj`, `NLISSN.csproj`, and `CpgPersistenceBenchmark.csproj` build successfully.
- The NLCPG CLI runs with its renamed project path and produces the default graph.
- The NLCPG-focused contract run compiles and executes 77 tests; 75 pass. Two existing
  interprocedural-dataflow assertions remain outside this rename's scope.
- UnitTests builds successfully. HostTests and PerformanceTests retain independent deletion
  pipeline API drift that does not mention the retired CPG identity.
