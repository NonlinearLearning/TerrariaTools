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
