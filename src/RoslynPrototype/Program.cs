using RoslynPrototype.Application;

var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());
var result = await host.AnalyzeFromArgsAsync(args);
var formatter = new DeletionResultFormatter();

foreach (var line in formatter.FormatResult(result))
{
    Console.WriteLine(line);
}
