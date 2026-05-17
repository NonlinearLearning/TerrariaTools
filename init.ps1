$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_HOME = $repoRoot

Write-Host "[init] repo root: $repoRoot"
Write-Host "[init] DOTNET_CLI_HOME: $env:DOTNET_CLI_HOME"

$projectCandidates = @(
  "src\MinimalRoslynCpg\MinimalRoslynCpg.csproj",
  "NewJoern\NewJoern.csproj"
)

$projectPath = $null
foreach ($candidate in $projectCandidates) {
  $candidatePath = Join-Path $repoRoot $candidate
  if (Test-Path $candidatePath) {
    $projectPath = $candidatePath
    break
  }
}

if ($null -eq $projectPath) {
  $searched = $projectCandidates -join ", "
  throw "[init] Missing project file. Searched: $searched"
}

Write-Host "[init] project file: $projectPath"

$globalJsonPath = Join-Path $repoRoot "global.json"
if (Test-Path $globalJsonPath) {
  $globalJson = Get-Content $globalJsonPath -Raw | ConvertFrom-Json
  Write-Host "[init] requested sdk: $($globalJson.sdk.version)"
}

$dotnetVersion = & dotnet --version
Write-Host "[init] dotnet version: $dotnetVersion"

Write-Host "[init] running build health check..."
$buildOutput = & dotnet build $projectPath 2>&1
$buildExitCode = $LASTEXITCODE
$buildText = ($buildOutput | Out-String).Trim()

if ($buildExitCode -eq 0) {
  Write-Host "[init] build check passed"
  exit 0
}

if ($buildText -match "Value cannot be null\. \(Parameter 'path1'\)") {
  Write-Warning "[init] build check hit the known restore blocker: NuGet.targets path1 null"
  Write-Host "[init] source-level work can continue, but end-to-end verification is still blocked"
  exit 0
}

Write-Error "[init] build health check failed with an unexpected error:`n$buildText"
exit $buildExitCode
