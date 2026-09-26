// 可执行入口故意保持极薄，只把参数处理和输出职责交给 CLI 宿主。
using NLCPG.Cli;

if (args.Length != 0)
{
    throw new ArgumentException(
      "NLCPG reads configuration from nlissn.yml and accepts no command-line parameters.");
}

return new NLCPGCli().Run(args);
