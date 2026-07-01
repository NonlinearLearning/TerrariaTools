# Samples

## Run the sample

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj .\src\MinimalRoslynCpg\samples\analysis-sample.cs
```

## Run a local CPG view

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj `
  .\src\MinimalRoslynCpg\samples\analysis-sample.cs `
  --view local `
  --anchor-name Normalize `
  --hops 1
```

The sample includes:

- field access
- binary operations
- local variables
- method invocation
- `if` control structure
- `while` loop
- return statements
