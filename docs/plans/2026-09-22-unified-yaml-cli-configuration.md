# Unified YAML CLI Configuration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move all business CLI controls for NLISSN, NLCPG, and NLCPG.ProjectExport into one strict `nlissn.yml` document selected by a `tool` discriminator.

**Architecture:** Keep NLISSN's existing domain-aware loader for its branch and add local Schema 3 adapters to NLCPG and ProjectExport to avoid the existing NLISSN.Core/NLCPG dependency cycle. All three executables reject arguments and load the fixed working-directory file; programmatic APIs remain usable.

**Tech Stack:** C#/.NET 10, YamlDotNet, JSON Schema Draft 2020-12, xUnit, PowerShell harness scripts.

---

### Task 1: Add the unified Schema 3 contract and migration fixtures

**Files:**
- Create: `Miscellaneous/schemas/nlissn.schema.3.json`
- Modify: `Miscellaneous/schemas/README.md`
- Modify: `Miscellaneous/nlissn.yml`
- Test: `tests/NLISSN.ContractTests/Configuration/NlissnSchemaContractTests.cs`

**Steps:**

1. Add a failing schema contract test for `schemaVersion: 3`, the required `tool` enum, the three tool branches, and rejection of unknown branch properties.
2. Run the focused contract selection and confirm it fails because Schema 3 and the branches do not exist.
3. Add the Schema 3 JSON contract with conditional required fields for `nlissn`, `nlcpg`, and `nlcpg-project-export`; retain the old Schema 2 file as the historical/compatibility schema.
4. Migrate the repository example to `schemaVersion: 3` and `tool: nlissn` without changing its existing analysis or six concurrency settings.
5. Update the Schema README with the unified file name, tool routing, field mappings, and relative path rules.
6. Run the schema contract tests, JSON parse validation, and `git diff --check`.

### Task 2: Implement NLCPG YAML loading and argument rejection

**Files:**
- Modify: `src/NLCPG/NLCPG.csproj`
- Create: `src/NLCPG/Cli/NLCPGYamlConfiguration.cs`
- Modify: `src/NLCPG/Cli/NLCPGCli.cs`
- Modify: `src/NLCPG/Program.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGDisplayTextTests.cs`

**Steps:**

1. Add tests that write a temporary `nlissn.yml` with `tool: nlcpg`, execute the configured stats/local-view path without arguments, and reject an argument or mismatched tool.
2. Run the focused NLCPG contract tests and confirm they fail against the current argument parser.
3. Add the YamlDotNet reference and a strict local loader for the `nlcpg` branch, including relative input/JSON paths, anchor exclusivity, direction, hops, and edge-kind arrays.
4. Change the executable entrypoint to reject non-empty `args` and load the working-directory configuration.
5. Keep graph construction and output logic unchanged, replacing only the argument-derived `CliOptions` with loaded options.
6. Run the NLCPG owning build, focused CLI contract tests, and an argument-free stats/local-view smoke.

### Task 3: Implement ProjectExport YAML loading and argument rejection

**Files:**
- Modify: `src/NLCPG.ProjectExport/NLCPG.ProjectExport.csproj`
- Create: `src/NLCPG.ProjectExport/ProjectExportYamlConfiguration.cs`
- Modify: `src/NLCPG.ProjectExport/Program.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportTests.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportConcurrencyTests.cs`

**Steps:**

1. Add tests for the former `--project`, output, target framework, MSBuild, restore, generated-source, worker, resume, and tool-mismatch settings in a temporary unified YAML file.
2. Run the focused ProjectExport selections and confirm the new configuration path fails before implementation.
3. Add the YamlDotNet reference and a strict ProjectExport branch loader mapping `input` and `projectExport` into `ProjectExportOptions`.
4. Remove the production argument parser and make the executable reject all arguments before loading `nlissn.yml`.
5. Preserve the worker cap, project exporter behavior, output defaults, and programmatic `ProjectExportOptions` constructor.
6. Run the ProjectExport owning build, focused contract tests, and an argument-free isolated export smoke.

### Task 4: Upgrade the NLISSN production loader to the unified branch

**Files:**
- Modify: `src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs`
- Modify: `src/NLISSN/Program.cs`
- Modify: `tests/NLISSN.HostTests/Configuration/YamlConfigurationLoaderTests.cs`
- Modify: `tests/NLISSN.ContractTests/Configuration/NlissnSchemaContractTests.cs`

**Steps:**

1. Add tests for `tool: nlissn`, schema 3 loading, mismatched tool rejection, and preservation of the existing Schema 2 compatibility boundary.
2. Run the focused YAML loader tests and confirm the tool/version assertions fail.
3. Add the Schema 3/tool validation while preserving existing path, artifact, replay, duplicate-key, and six-concurrency validations.
4. Ensure NLISSN production execution still rejects all command-line arguments and uses the unified file.
5. Run the YAML loader, NLISSN host, and current CLI smoke tests.

### Task 5: Synchronize documentation and harness references

**Files:**
- Modify: `docs/cli-reference.md`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`
- Modify: `docs/README.md`
- Modify: `Miscellaneous/schemas/README.md`
- Modify: `Miscellaneous/scripts/check-harness-consistency.ps1` if required by its current assertions

**Steps:**

1. Replace ProjectExport and NLCPG argument examples with `nlissn.yml` examples and argument-free commands.
2. Document the `tool` discriminator and all former CLI-to-YAML mappings.
3. Remove stale references to ProjectExport command-line switches and standalone config filenames.
4. Run the documentation link checks, harness consistency script, and `git diff --check`.

### Task 6: Run final focused verification

**Files:**
- Modify: `Context/progress.md`

**Steps:**

1. Run the owning builds for `NLCPG`, `NLCPG.ProjectExport`, `NLISSN`, and affected Contract/Host projects with shared compilation disabled where required.
2. Run focused Schema, NLCPG CLI, ProjectExport CLI, and NLISSN loader tests.
3. Run argument-free smoke tests for all three executables from isolated configuration directories.
4. Run `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1` and `git diff --check`.
5. Record only executed evidence and remaining unverified long-running boundaries in `Context/progress.md`.

