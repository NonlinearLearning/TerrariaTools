# Cli 层

`src/Cli` 是唯一进程入口。

## 职责

- 解析命令行
- 解析 `--config`
- 构造 `RunRequest`
- 输出帮助和错误信息
- 把 `FailureCode` 映射为退出码

## 当前入口

- `Program.cs`
- `DomeCliParser.cs`

## 当前标准命令

- `run`
- `analyze`
- `plan`

标准 CLI 不再提供 legacy runtime 命令。
