using NLISSN.Infrastructure.Configuration;

namespace NLISSN.Hosting;

internal sealed class ConfigurationRunHost
{
  internal async Task RunAsync()
  {
    var configuration = YamlConfigurationLoader.LoadFromWorkingDirectory();
    YamlConfigurationLoader.PrepareRunArtifacts(configuration);
    var host = new CommandHost();
    await host.AnalyzeAsync(configuration);
  }
}
