# FormatTool Parameter List Design

> For this task, the implementation stays in the current session and targets a small standalone Roslyn-based formatter.

**Goal:** Build a standalone tool under `FormatTool/` that rewrites multiline C# declaration parameter lists onto one line.

**Architecture:** The tool scans a target path for `*.cs` files, parses each file with Roslyn, rewrites declaration parameter lists with a targeted syntax rewriter, and writes files back only when text changed. The rewriter only touches declaration parameter lists and skips lists that contain directives or comments so it does not accidentally destroy hand-written trivia.

**Tech Stack:** .NET 10, `Microsoft.CodeAnalysis.CSharp`

---

## Scope

- Format `MethodDeclarationSyntax`
- Format `ConstructorDeclarationSyntax`
- Format `LocalFunctionStatementSyntax`
- Format `DelegateDeclarationSyntax`
- Do not add `--dry-run`
- Do not run a general-purpose formatter

## Verification

1. `dotnet build .\FormatTool\FormatTool.csproj`
2. `dotnet run --project .\FormatTool\FormatTool.csproj -- .\src\Rules\RuleAnalysisHelpers.cs`
3. Confirm the affected declaration parameter lists in `src\Rules\RuleAnalysisHelpers.cs` are on one line
