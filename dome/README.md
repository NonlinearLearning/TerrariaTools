# Dome v1

`dome` is a Roslyn-based code transformation tool. Its execution flow is fixed:

`Analysis -> Mark -> Plan -> Rewrite -> Report`

v1 is plan-driven. `Rewrite` only executes `audit-plan.json` and does not infer extra changes.

## Quick Start

Build the CLI:

```powershell
dotnet build .\src\Cli\Dome.Cli.csproj -nologo
```

Run a full pipeline:

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples .\out
```

Generate analysis only:

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- analyze .\samples .\out
```

Generate a plan without rewrite:

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples .\out
```

Run from a config file:

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- --config .\dome.config.json
```

## Commands

- `run <input-path> <output-path>`
  Runs analysis, marking, plan compilation, rewrite, and reporting.
- `analyze <input-path> <output-path>`
  Writes `analysis.json` and `report.json`.
- `plan <input-path> <output-path>`
  Writes `audit-plan.json` and `report.json`.
- `--config <path>`
  Loads the same inputs from JSON.

`input-path` can be a single `.cs` file or a directory tree. Directory inputs preserve relative paths in `audit-plan.json` and `rewritten/**`.

## Config File

Minimal config:

```json
{
  "Command": "run",
  "InputPath": "D:\\input",
  "OutputPath": "D:\\output",
  "RuleSet": [],
  "LogLevel": "Info"
}
```

Supported fields:

- `Command`: `run`, `analyze`, `plan`
- `InputPath`: file or directory
- `OutputPath`: output root
- `RuleSet`: reserved for future rule-set selection; accepted in v1
- `LogLevel`: reserved for future logging selection; accepted in v1

## Output Artifacts

`analyze`

- `analysis.json`
- `report.json`

`plan`

- `audit-plan.json`
- `report.json`

`run`

- `audit-plan.json`
- `report.json`
- `rewritten/**`

See [architecture.md](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/architecture.md) and [artifacts.md](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/artifacts.md) for the stable v1 contract.

## Exit Codes

- `0`: success
- `1`: CLI argument or config parse failure
- `2`: `WorkspaceLoadFailed`
- `3`: `AnalysisFailed`
- `4`: `PlanCompileFailed`
- `5`: `RewriteFailed`
- `6`: `ReportFailed`

## Failure Modes

`report.json` is the primary failure summary. In v1 it carries:

- `FailureCode`
- `FailureSummary`
- `ConflictSummaries`
- `RiskSummary`
- `GeneratedArtifacts`

Typical failure cases:

- no C# input files found
- analysis error
- unresolved plan conflict
- rewrite target member not found
- rewrite target span mismatch
- rewrite target text mismatch
- unsupported action/target combination

Plan conflicts do not produce a fake success plan. Rewrite failures still write `report.json` and preserve the compiled `audit-plan.json`.

## Multi-file Input Example

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\input-project .\out
```

If `input-project` contains:

- `Root.cs`
- `Features\Nested.cs`

then `run` writes:

- `out\audit-plan.json`
- `out\report.json`
- `out\rewritten\Root.cs`
- `out\rewritten\Features\Nested.cs`

## Known Limits

- No checkpoint or resume support
- No in-place rewrite
- No dynamic plugins
- No HTML/UI report output
- No first-class lambda/local-function targeting
- Rewrite remains statement-oriented
- Initializer analysis is supported, but initializer rewrite is not part of v1

## Related Docs

- [Architecture Overview](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/architecture.md)
- [Artifact Contract](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/artifacts.md)
- [Original design record](/D:/ProjectItem/SourceCode/Net/TerrariaTools/docs/plans/2026-03-12-dome-architecture-design.md)
