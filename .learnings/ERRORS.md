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
