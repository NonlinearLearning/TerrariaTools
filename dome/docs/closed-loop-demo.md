# Dome 闭环样例

这份文档只说明当前仓库里几组最有代表性的样例，以及它们分别验证什么。

## 1. `closed-loop`

路径：

- `samples/closed-loop`

目的：

- 验证最简单的 directive -> plan -> rewrite 闭环

关注点：

- `audit-plan.json` 中出现 statement delete
- `report.json` 标记运行成功
- `rewritten/**` 中对应语句消失

## 2. `expression-loop`

路径：

- `samples/expression-loop`

目的：

- 验证 expression projection + propagation 的闭环

关注点：

- statement delete 来自 `expression-mark`
- 传播删除来自 `dataflow-propagation`
- rewrite 后依赖表达式的语句也被清理

## 3. `sanitization-loop`

路径：

- `samples/sanitization-loop`

目的：

- 验证 sanitization 会截断传播

关注点：

- 前半段传播成立
- sanitizing assignment 之后的 target 不应继续被删除

## 4. `protection-loop`

路径：

- `samples/protection-loop`

目的：

- 验证 protection rule 会阻断传播边界

关注点：

- protected target 自身不进入计划
- protected boundary 后的传播被截断

## 5. `promotion-loop`

路径：

- `samples/promotion-loop`

目的：

- 验证 direct statement delete 会触发 boundary promotion

关注点：

- plan 中同时出现 statement target 和 method target
- rewrite 后相关方法被删掉或失效
