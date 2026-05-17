$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function Read-Text([string] $relativePath) {
  $fullPath = Join-Path $repoRoot $relativePath
  if (-not (Test-Path $fullPath)) {
    throw "Missing file: $relativePath"
  }

  return Get-Content $fullPath -Raw
}

function Require-Contains([string] $content, [string] $expected, [string] $label) {
  if (-not $content.Contains($expected)) {
    throw "[$label] Missing expected text: $expected"
  }
}

$programText = Read-Text "NewJoern/Program.cs"
$quickStartText = Read-Text "NewJoern/docs/quick-start.md"
$cliReferenceText = Read-Text "NewJoern/docs/cli-reference.md"
$developerGuideText = Read-Text "NewJoern/docs/developer-guide.md"
$agentsText = Read-Text "AGENTS.md"
$progressText = Read-Text "progress.md"
$verificationText = Read-Text "NewJoern/docs/verification-playbook.md"

$usageLines = @(
  'Console.WriteLine("  NewJoern parse <input-path> [--json-out <path>]");',
  'Console.WriteLine("  NewJoern export <input-path> --format <json|dot> --out <path>");',
  'Console.WriteLine("  NewJoern slice <input-path> [--mode data-flow|usages] --out <path> ...");'
)

$docUsageLines = @(
  'NewJoern parse <input-path> [--json-out <path>]',
  'NewJoern export <input-path> --format <json|dot> --out <path>',
  'NewJoern slice <input-path> [--mode data-flow|usages] --out <path> ...'
)

foreach ($usageLine in $usageLines) {
  Require-Contains $programText $usageLine "Program.cs"
}

foreach ($docUsageLine in $docUsageLines) {
  Require-Contains $quickStartText $docUsageLine "quick-start.md"
  Require-Contains $cliReferenceText $docUsageLine "cli-reference.md"
}

$verifiedBuildText = 'dotnet build .\NewJoern\NewJoern.csproj'
Require-Contains $agentsText 'pwsh -File .\init.ps1' 'AGENTS.md'
Require-Contains $progressText 'pwsh -File .\init.ps1' 'progress.md'
Require-Contains $verificationText 'pwsh -File .\init.ps1' 'verification-playbook.md'
Require-Contains $verificationText $verifiedBuildText 'verification-playbook.md'

if ($quickStartText.Contains("Value cannot be null. (Parameter 'path1')")) {
  throw "[quick-start.md] Still contains the stale restore blocker text."
}

if ($developerGuideText.Contains("Value cannot be null. (Parameter 'path1')")) {
  throw "[developer-guide.md] Still contains the stale restore blocker text."
}

Write-Host "[check-harness-consistency] OK"
