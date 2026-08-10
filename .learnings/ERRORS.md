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
