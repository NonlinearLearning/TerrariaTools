## [LRN-20260704-001] correction

**Logged**: 2026-07-04T00:00:00+08:00
**Priority**: medium
**Status**: pending
**Area**: docs

### Summary
仓库内批量补代码注释时，默认应使用中文注释

### Details
本次给 `src/MinimalRoslynCpg` 批量补注释时，先落了英文 XML 摘要。用户随后明确更正为“中文注释”。后续在这个仓库里做注释、说明或文档式代码摘要时，应优先直接使用中文，避免再做一次语言回改。

### Suggested Action
涉及仓库内代码注释的批量修改前，先检查现有注释语言，并默认沿用中文。

### Metadata
- Source: user_feedback
- Related Files: src/MinimalRoslynCpg
- Tags: correction, comments, language

---
