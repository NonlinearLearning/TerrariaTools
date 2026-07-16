using RoslynPrototype.Application;

var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());
await host.AnalyzeFromArgsAsync(args);
