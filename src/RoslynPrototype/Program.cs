using RoslynPrototype.Application;

var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
var result = application.AnalyzeFromArgs(args);

foreach (var line in application.FormatResult(result)) {
  Console.WriteLine(line);
}
