# Test projects

- `RoslynDeletionPrototype.Testing` owns reusable C# input assets and test infrastructure only.
- `UnitTests` owns rule-local and pure graph behavior; it must not reference Host.
- `ContractTests` owns frozen-graph, persistence, rewrite, and architectural contracts.
- `HostTests` owns command-host, directory, logging, and full-rule-pipeline behavior.
- `PerformanceTests` owns deterministic performance regressions. External Terraria inputs are opt-in and stay outside routine `dotnet test` execution.

Run the smallest owning project first, then `pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast`, `-Host`, or `-Performance` as appropriate. Test names use `Method_Scenario_ExpectedResult`; source fixtures remain in `RoslynDeletionPrototype.Testing`.
