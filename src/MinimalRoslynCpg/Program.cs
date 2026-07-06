// 可执行入口故意保持极薄，只把参数处理和输出职责交给 CLI 宿主。
using MinimalRoslynCpg.Cli;

return new MinimalRoslynCpgCli().Run(args);
