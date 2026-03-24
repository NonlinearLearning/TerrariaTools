# 测试指南

本页说明当前仓库的测试结构，以及你在改动代码后应该补哪类测试。

## 测试项目

### `tests/Dome.Tests`

主测试项目。这里包含：

- 单元测试
- 集成测试
- 契约测试
- golden tests

### `tests/TerrariaTools.Testing`

测试基建项目。这里包含：

- 断言帮助器
- 测试数据构建器
- 测试替身
- Verify 配置

## 当前常见测试分类

你会在 `tests/Dome.Tests` 下看到这些目录：

- `Analysis/`
- `Application/`
- `Cli/`
- `Plan/`
- `Reporting/`
- `Rewrite/`
- `Rules/`

这些目录基本对应生产代码的主模块。

## 什么时候补哪类测试

### 改了 Core 模型或规则

优先补：

1. 单元测试
2. 规则切片测试
3. 计划编译测试

### 改了 Application pipeline

优先补：

1. 用例单元测试
2. 编排测试
3. 必要时补集成测试

### 改了 Adapters

优先补：

1. 单元测试
2. 契约测试
3. 必要时补 golden tests

### 改了 CLI

优先补：

1. 参数解析测试
2. 分发测试

## Golden tests

仓库使用 `Verify.Xunit` 做 golden output 校验。适合下面这些场景：

- JSON 报告结构
- 重写后源码
- 稳定文本输出

如果你的改动会改变输出形状，优先考虑 golden test，而不是只写字符串包含断言。

## 推荐顺序

当你修改一条主流程时，按下面的顺序补测试：

1. 先补离改动最近的单元测试。
2. 再补能覆盖编排行为的测试。
3. 最后补一条最小集成测试，确认产物和路径没有偏移。

## 运行建议

从 `dome/` 目录执行：

```bash
dotnet test tests/Dome.Tests/Dome.Tests.csproj
```

如果你只想缩小范围，可以直接指定测试文件所属命名空间或测试名称过滤器。
