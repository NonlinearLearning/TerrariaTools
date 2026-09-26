[CmdletBinding()]
param(
  [string]$OutputDirectory,
  [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:DOTNET_CLI_HOME = $repoRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  $OutputDirectory = Join-Path $repoRoot ('Build/DataFlowTailMeasurement/run-' +
    [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
  throw "Use a fresh output directory; existing evidence is preserved: $OutputDirectory"
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$buildElapsedMs = 0.0
if (-not $NoBuild) {
  $buildWatch = [Diagnostics.Stopwatch]::StartNew()
  & dotnet build (Join-Path $repoRoot 'tools/DataFlowTailMeasurement/DataFlowTailMeasurement.csproj') -c Release -p:UseSharedCompilation=false *> (Join-Path $OutputDirectory 'build.log')
  $buildExitCode = $LASTEXITCODE
  $buildWatch.Stop()
  $buildElapsedMs = $buildWatch.Elapsed.TotalMilliseconds
  @{ ExitCode = $buildExitCode; ElapsedMs = $buildElapsedMs } | ConvertTo-Json |
    Set-Content (Join-Path $OutputDirectory 'build-result.json')
  if ($buildExitCode -ne 0) { throw "Build failed; see $OutputDirectory/build.log" }
}

Push-Location $repoRoot
try {
  $sourcePaths = @(& rg --files src tools/DataFlowTailMeasurement tests/NLISSN.Testing/TestCodeSet/Cpg) | Where-Object {
    $_ -match '\.(cs|csproj|props|targets)$' -and $_ -notmatch '[/\\](bin|obj)[/\\]'
  }
  $sourcePaths += 'Directory.Build.props', 'global.json',
    'Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1'
  $sourceHashes = [ordered]@{}
  foreach ($sourcePath in ($sourcePaths | Sort-Object -Unique)) {
    $sourceHashes[$sourcePath] = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
  }
  [ordered]@{
    Head = (& git rev-parse HEAD)
    Branch = (& git branch --show-current)
    Status = @(& git status --porcelain=v1)
    SourceHashes = $sourceHashes
  } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'source-state.json')

  $processInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
  $processInfo.WorkingDirectory = $repoRoot
  $processInfo.ArgumentList.Add((Join-Path $repoRoot 'Build/tools/Release/net10.0/DataFlowTailMeasurement.dll'))
  $processInfo.ArgumentList.Add($OutputDirectory)
  $processInfo.UseShellExecute = $false
  $processInfo.CreateNoWindow = $true
  $processInfo.RedirectStandardOutput = $true
  $processInfo.RedirectStandardError = $true
  # Fixed JIT policy for every mode; no background tier promotion between paired samples.
  $processInfo.Environment['DOTNET_TieredCompilation'] = '0'
  $processInfo.Environment['DOTNET_ReadyToRun'] = '1'
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $processInfo
  $processWatch = [Diagnostics.Stopwatch]::StartNew()
  [void]$process.Start()
  $stdout = $process.StandardOutput.ReadToEndAsync()
  $stderr = $process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit(45000)) {
    $process.Kill($true)
    $process.WaitForExit()
    @{ Status = 'Incomplete'; Reason = 'Parent process 45-second deadline' } |
      ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'parent-timeout.json')
  }
  $processWatch.Stop()
  $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'stdout.log')
  $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'stderr.log')
  $sourceUnchanged = $true
  foreach ($sourcePath in $sourceHashes.Keys) {
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $sourceHashes[$sourcePath]) {
      $sourceUnchanged = $false
    }
  }
  [ordered]@{
    ExitCode = $process.ExitCode
    ProcessElapsedMs = $processWatch.Elapsed.TotalMilliseconds
    BuildElapsedMs = $buildElapsedMs
    SourceUnchanged = $sourceUnchanged
  } | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'process-result.json')
  Get-Content (Join-Path $OutputDirectory 'stdout.log')
  Write-Host "Evidence: $OutputDirectory"
  if ($process.ExitCode -ne 0 -or -not $sourceUnchanged) {
    throw "Measurement incomplete; inspect the preserved evidence directory."
  }
} finally {
  Pop-Location
}
