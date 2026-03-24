# Build and Test

This guide documents the current `dome/` build and verification entry points.

## Prerequisites

- Install the .NET 10 SDK.
- Run commands from the `dome/` directory. If you start from the repository root, add the `dome/` prefix to paths.

## Restore

```bash
dotnet restore dome.sln
```

## Build

Build the full solution:

```bash
dotnet build dome.sln
```

Build the core CPG package directly:

```bash
dotnet build src/Core/CPG/Dome.Core.Cpg.csproj
```

## Test

Run the application-level test suite:

```bash
dotnet test tests/Dome.Tests/Dome.Tests.csproj
```

Run the dedicated CPG suite:

```bash
dotnet test src/Core/CPG/Tests/JoernishCpg.Tests.csproj
```

## CLI Entry Points

The standard CLI host is `apps/Dome.Cli`.

### `run`

```bash
dotnet run --project apps/Dome.Cli -- run <input-path> <output-path>
```

### `analyze`

```bash
dotnet run --project apps/Dome.Cli -- analyze <input-path> <output-path>
```

### `plan`

```bash
dotnet run --project apps/Dome.Cli -- plan <input-path> <output-path>
```

### `tr-run`

```bash
dotnet run --project apps/Dome.Cli -- tr-run <solution-path> <output-path>
```

### `tr-shadow`

```bash
dotnet run --project apps/Dome.Cli -- tr-shadow <solution-path> <output-path> <seed-member-name>
```

## Loader Options

The standard flow supports these loader switches:

```text
--loader auto|codeanalysis|sourceonly
--no-fallback
```

Recommended usage:

1. For a `.sln` or `.csproj`, start with the default `auto`.
2. For a single `.cs` file or a small sample directory, prefer `--loader sourceonly`.
3. Only add `--no-fallback` when you explicitly want to disable source fallback.

## Examples

Run the standard flow on a sample directory:

```bash
dotnet run --project apps/Dome.Cli -- run samples/closed-loop out/closed-loop --loader sourceonly
```

Analyze a sample without rewriting:

```bash
dotnet run --project apps/Dome.Cli -- analyze samples/expression-loop out/expression-loop --loader sourceonly
```

Run the Terraria runtime flow:

```bash
dotnet run --project apps/Dome.Cli -- tr-run <TerrariaServer.sln> out/tr-run
```

Run Terraria shadow extraction:

```bash
dotnet run --project apps/Dome.Cli -- tr-shadow <TerrariaServer.sln> out/tr-shadow Terraria.Main.DedServ
```
