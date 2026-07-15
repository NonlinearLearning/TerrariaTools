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
