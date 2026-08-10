using NLISSN.Hosting;

if (args.Length != 0)
{
  throw new ArgumentException(
    "NLISSN reads configuration from nlissn.yml and accepts no command-line parameters.");
}

await new ConfigurationRunHost().RunAsync();
