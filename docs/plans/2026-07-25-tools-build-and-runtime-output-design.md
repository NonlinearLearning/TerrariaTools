# Tool Build And Runtime Output Design

## Goal

Place both tool build artifacts and default runtime reports under the repository
`Build/tools/` tree, while keeping explicit output paths available for large
benchmark runs.

## Directory Contract

```text
Build/tools/
  CpgPersistenceBenchmark/
    Debug|Release/net10.0/
    obj/CpgPersistenceBenchmark/
    reports/
    runs/<run-id>/
  CpgMicrobenchmarks/
    Debug|Release/net10.0/
    obj/CpgMicrobenchmarks/
    BenchmarkDotNet/
```

`Directory.Build.props` remains the single build-artifact policy. The
persistence benchmark writes its default JSON report below `reports/`; its
`--output` option remains authoritative. The PowerShell runner defaults to a
unique directory below `runs/`, while an explicitly supplied `OutputRoot`
remains supported. BenchmarkDotNet receives an explicit artifacts path below
the microbenchmark directory.

## Verification

- Add a configuration regression for the persistence benchmark's default report
  path and preserve its explicit `--output` behavior.
- Verify the runner's default root through a focused PowerShell test or
  invocation that does not start a real benchmark.
- Run the focused performance-test project, build both tool projects, and
  inspect the effective MSBuild output paths.
