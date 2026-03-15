# Testing Guidelines

## Structure

- `tests\Dome.Tests` uses explicit test-purpose directories:
  - `Analysis`: `Contracts`, `Integration`, `Unit`
  - `Application`: `Integration`, `Unit`
  - `Cli`: `Unit`, `Integration`
  - `Plan`: `Unit`
  - `Reporting`: `Contracts`, `Unit`
  - `Rewrite`: `Contracts`, `Golden`, `Unit`
  - `Rules`: `Slice`, `Unit`
  - `DomeTesting`: `TestBuilders`, `TestDoubles`
- New tests should be classified in this order:
  - `pure model`
  - `fake-driven orchestration`
  - `Roslyn slice`
  - `true IO integration`
  - `contract`
  - `golden`
- Domain roots such as `Analysis`, `Application`, `Cli`, `Plan`, `Reporting`, `Rewrite`, and `Rules` must not contain direct test `.cs` files.
- Snapshot baselines (`*.verified.*`, `*.received.*`) must stay next to their owning tests and must not live under `tests\TerrariaTools.Testing`.

## Hardening

- `Application/Integration` keeps only true end-to-end samples. A test in that area must validate at least two of:
  - default factory wiring
  - real Roslyn analysis
  - real rewrite output
  - real artifact writing
  - real workspace behavior
  - real build-chain behavior
- `Application/Integration` should not carry failure-only or summary-only assertions when the same behavior can be verified through unit/orchestration seams.
- `Application/Unit` and `Cli/Unit` must not directly use:
  - `Path.GetTempPath()`
  - `Directory.CreateDirectory(...)`
  - `File.WriteAllText*`
  - `File.ReadAllText*`
- When a unit test needs real environment behavior, it must go through a fixture, builder, or helper layer instead of inlining file-system setup in the test body.
- `DomeTesting` is for Dome-specific scenario support only.
- Shared patterns such as `Recording*`, generic `Fake*Store`, and generic `*Builder` belong in `TerrariaTools.Testing`.
