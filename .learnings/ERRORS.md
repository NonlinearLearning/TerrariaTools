## [ERR-20260806-NPC-PATH] nlissn_single_file_analysis

**Logged**: 2026-08-06T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: config

### Summary
The first NLISSN run used an extra `Terraria` path segment and failed before analysis.

### Error
`NLISSN130 input.path: does not exist: D:\\ProjectItem\\SourceCode\\TR\\Terraria\\Terraria\\NPC.cs`

### Context
- Intended target: `D:\\ProjectItem\\SourceCode\\TR\\Terraria\\NPC.cs`
- The independent run configuration was created under `Build\\analysis\\npc-rule-20260806`.

### Suggested Fix
Validate the exact absolute input path before starting the rule host.

### Metadata
- Reproducible: yes
- Related Files: Build/analysis/npc-rule-20260806/nlissn.yml

### Resolution
- **Resolved**: 2026-08-06
- **Notes**: Corrected the configuration path before rerunning.

---
## [ERR-20260909-MARKDOWN-LINK-LINE] markdown-link-check

**Logged**: 2026-09-09T22:29:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The first successful parse of the local Markdown-link checker treated repository links with a trailing `:line` locator as literal filenames.

### Error
```text
docs/research/2026-09-09-analysis-pipeline-performance-study.md -> ../../src/NLISSN/Hosting/CommandHost.cs:38
```

### Context
- The report uses repository file links with one-based line locators.
- The checker removed URL fragments but did not remove the optional numeric line suffix before calling `Test-Path`.
- The referenced source files exist at the path before `:line`.

### Suggested Fix
Strip a trailing `:\d+` locator from relative local targets before resolving the filesystem path.

### Metadata
- Reproducible: yes
- Related Files: docs/research/2026-09-09-analysis-pipeline-performance-study.md

### Resolution
- **Resolved**: 2026-09-09
- **Notes**: Updated the one-off checker expression to normalize `path:line` targets before existence checks.

---
## [ERR-20260909-POWERSHELL-QUOTE] markdown-link-check

**Logged**: 2026-09-09T22:28:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The first local Markdown-link check failed because an outer PowerShell command expanded inner `$` variables before passing the script to `pwsh`.

### Error
```text
ParserError: Missing variable name after foreach. The correct form is: foreach ($a in $b) {...}
```

### Context
- The check was passed as a double-quoted `pwsh -Command` argument from an outer PowerShell process.
- The outer process removed `$docPaths`, `$broken`, and loop variables before the inner parser received the command.
- No repository content was modified by the failed check.

### Suggested Fix
Use an outer single-quoted command string with inner double-quoted path literals, or run the check from a temporary script file when nested PowerShell syntax is required.

### Metadata
- Reproducible: yes
- Related Files: docs/research/2026-09-09-analysis-pipeline-performance-study.md; 设计docs/目前设计/性能分析组件.md

### Resolution
- **Resolved**: 2026-09-09
- **Notes**: Rewrote the checker invocation so the inner PowerShell variables remain intact.

---

## [ERR-20260909-GITHUB-EOF] github-raw-fetch

**Logged**: 2026-09-09T18:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A parallel PowerShell fetch of GitHub raw source files ended with an unexpected EOF.

### Error
Received an unexpected EOF or 0 bytes from the transport stream.

### Context
- Attempted to fetch LLVM LNT, LLVM Test Suite, and Roslyn benchmark sources with Invoke-WebRequest.
- The failure affected the transport request; it did not establish that any source URL was unavailable.

### Suggested Fix
Retry through curl.exe or the GitHub HTML/API endpoint and record only facts confirmed by the returned source.

### Metadata
- Reproducible: unknown
- Related Files: docs/research/2026-09-09-analysis-pipeline-performance-study.md

---

### Resolution
- **Resolved**: 2026-09-09
- **Notes**: GitHub HTML/blob pages returned HTTP 200 and were used to verify the added source links; raw/curl transport remained unreliable, so no raw response was treated as evidence.

## [ERR-20260909-DOTNET-LOCK] parameter-shrink-test-build

**Logged**: 2026-09-09T18:00:00+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
The targeted ParameterShrink regression build failed because NLCPG.dll was locked by another compiler process.

### Error
    CSC : error CS2012: Cannot open Build/src/obj/NLCPG/Debug/net10.0/NLCPG.dll for writing because it is being used by another process.

### Context
- Command: dotnet test tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj --filter FullyQualifiedName~PerformanceOptimizationRegressionTests --no-restore
- The failure occurred during project compilation before the test filter ran.

### Suggested Fix
Retry with a single build process or an isolated intermediate/output directory after confirming no stale compiler process holds the file.

### Metadata
- Reproducible: unknown
- Related Files: tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj

---

## [ERR-20260909-TEMP-PROBE] temporary-parameter-shrink-test

**Logged**: 2026-09-09T18:10:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The temporary review probe initially failed on an invalid C# interface assertion and was corrected before the probe passed.

### Error
The first probe run used Assert.IsType<IMethodSymbol> for a Roslyn public wrapper whose runtime type is an implementation type.

### Context
- Temporary file: tests/NLISSN.PerformanceTests/ParameterShrinkReviewTemporaryTests.cs
- The probe was deleted after verification.

### Suggested Fix
Use Assert.IsAssignableFrom for Roslyn interface symbols in temporary tests.

### Metadata
- Reproducible: no
- Related Files: tests/NLISSN.PerformanceTests/ParameterShrinkReviewTemporaryTests.cs

## [ERR-20260909-MSBUILD-COMPILE-GENERATED-PATH] global-compiler-generated-path

**Logged**: 2026-09-09T16:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
Passing `CompilerGeneratedFilesOutputPath` as a global MSBuild property caused dependent projects to pass the directory as a compiler input.

### Error
```text
CSC : error CS2021: 文件名“D:\ProjectItem\SourceCode\Net\NL\Build\src\obj\NLISSN.Rules\generated\”为空、包含无效字符...
```

### Context
- The property was supplied while rebuilding `src/NLISSN.Rules/NLISSN.Rules.csproj`.
- The value propagated into `NL.Caching` and `NL.Concurrency`.

### Suggested Fix
Do not pass the output-path property globally across project references; use the repository's normal build/test commands and rely on compiled catalog tests.

### Metadata
- Reproducible: yes
- Related Files: src/NLISSN.Rules/NLISSN.Rules.csproj

### Resolution
- **Resolved**: 2026-09-09
- **Notes**: Normal Rules build and focused tests remain the supported verification path.

---

## [ERR-20260909-PROJECT-PATH] linked_project_layout_scan

**Logged**: 2026-09-09T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
An architecture scan assumed `src/NLISSN.Rule` had a standalone project file, but the directory is intentionally linked into `NLISSN.Core`.

### Error
```text
Get-Content : Cannot find path 'src/NLISSN.Rule/NLISSN.Rule.csproj' because it does not exist.
```

### Context
- `src/NLISSN.Core/NLISSN.Core.csproj` links `..\NLISSN.Rule\*.cs` as `Rule\...`.
- The same project links `..\NLISSN.Application\ExecutionRuntime.cs` as `Pipeline\ExecutionRuntime.cs`.

### Suggested Fix
Inspect project files and linked compile items before treating a source directory as a project module.

### Metadata
- Reproducible: yes
- Related Files: src/NLISSN.Core/NLISSN.Core.csproj

---

## [ERR-20260909-PS-PIPE] cpg_file_size_scan

**Logged**: 2026-09-09T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A PowerShell architecture-scan helper emitted an empty pipeline while collecting CPG file sizes.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- The helper attempted to pipe a `foreach` statement directly into `Format-Table`.
- No repository command or source file was affected.

### Suggested Fix
Accumulate objects in a variable or use `ForEach-Object` before formatting the result.

### Metadata
- Reproducible: yes
- Related Files: src/NLCPG/Builder/NLCPGBuilder.cs

---

## [ERR-20260908-SKILL-PATH] skill_file_read

**Logged**: 2026-09-08T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The first attempt to read a skill file treated the `r0` skill-root alias as a literal directory.

### Error
```text
Cannot find path 'C:\Users\shan\.codex\skills\r0\verification-before-completion\SKILL.md'
```

### Context
- The available-skills listing defines `r0` as the root `C:\Users\shan\.codex\skills`.
- The correct path is `C:\Users\shan\.codex\skills\verification-before-completion\SKILL.md`.

### Suggested Fix
Expand the skill-root alias before appending the skill directory and `SKILL.md`.

### Metadata
- Reproducible: no
- Related Files: C:\Users\shan\.codex\skills\verification-before-completion\SKILL.md

### Resolution
- **Resolved**: 2026-09-08
- **Notes**: Read the skill from the expanded absolute path and continued verification.

---

## [ERR-20260909-TEMPLATE-ESCAPE] temp_change_record_patch

**Logged**: 2026-09-09T00:23:48+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The first apply_patch attempt for the temporary change record failed because Markdown backticks inside a JavaScript template literal were parsed as JavaScript syntax.

### Error
    SyntaxError: Unexpected identifier 'NLISSN'

### Context
- The patch was passed through functions.exec as a String.raw template literal.
- The patch text contained Markdown inline-code backticks around NLISSN.Application.
- No temporary or repository file was written by the failed attempt.

### Suggested Fix
Use patch text without backticks or escape every backtick before embedding it in the orchestration script.

### Metadata
- Reproducible: yes
- Related Files: C:/Users/shan/AppData/Local/Temp/architecture-review-change-20260909-002348.md

### Resolution
- **Resolved**: 2026-09-09
- **Notes**: Replaced the inline-code markup with plain text before retrying the patch.

---
## [ERR-20260909-DOTNET-VERBOSITY] dotnet-build-verbosity

**Logged**: 2026-09-09T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The repository plan's `dotnet build -v:min` spelling is rejected by this installed CLI.

### Error
```text
Cannot parse argument 'min' for option '--verbosity' ...
```

### Context
- `dotnet build .\\src\\NLISSN.Rules\\NLISSN.Rules.csproj --no-restore -v:min`
- Installed CLI accepts `-v:m` / `--verbosity minimal`.

### Suggested Fix
Use `-v:m` for local verification on this environment.

### Metadata
- Reproducible: yes
- Related Files: docs/plans/2026-09-08-rule-catalog-source-generator-execution.md

---
