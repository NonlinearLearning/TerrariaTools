# NLISSN scope

- CLI 入口保持薄：参数由 Host/Options 解析，规则编排在 Application。
- 修改删除规则链路前，先读根目录列出的删除规则与 atomic-marking 约束。
- rewrite 改动必须保留 `--no-diff`、`--skip-rewrite` 等无写入路径，并提供定向回归。
