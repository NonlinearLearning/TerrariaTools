# Rewrite 层

标准 Rewrite 位于 `src/Rewrite/Roslyn`。

## 职责

- 把 `AuditPlan` 映射回 Roslyn 语法树
- 在文档级别应用删除/替换/注释等动作
- 返回 `RewriteExecutionResult`

## 关键入口

- `RoslynRewriteExecutor.ExecuteAsync(...)`

## 输入

- `SourceDocumentSet`
- `AuditPlan`

## 输出

- `RewriteExecutionResult`
- `rewritten/**` 文件由 `Application` 负责落盘
