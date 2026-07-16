## [ERR-20260715-001] Start-Process standard-stream redirection

**Logged**: 2026-07-15T17:30:19+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
Background full-regression launch used the same file for standard output and standard error.

### Error
```
RedirectStandardOutput and RedirectStandardError are same. Give different inputs and Run your command again.
```

### Context
- Command: PowerShell `Start-Process dotnet test ...`
- No test process was created and no source files were changed.

### Resolution
- Use separate stdout and stderr log paths when running the regression in the background.

### Metadata
- Reproducible: yes
- Related Files: Build/full-regression-20260715.log

---

## [ERR-20260715-002] apply_patch empty move

**Logged**: 2026-07-15T18:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
`apply_patch` rejects a move-only update with no content hunk.

### Error
```
apply_patch verification failed: invalid hunk ... is empty
```

### Resolution
- Rename through an `apply_patch` move that also updates the archived file heading.

### Metadata
- Reproducible: yes
- Related Files: progress.md, progressinfo

---
