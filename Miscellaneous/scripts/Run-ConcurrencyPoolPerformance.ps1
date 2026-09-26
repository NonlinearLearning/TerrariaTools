param(
  [Parameter(Mandatory)]
  [string] $SourceFile,
  [Parameter(Mandatory)]
  [string] $TargetName,
  [string] $Dop = "1,8,12,16",
  [ValidateRange(0, [int]::MaxValue)]
  [int] $WarmupCount = 1,
  [ValidateRange(1, [int]::MaxValue)]
  [int] $MeasurementCount = 3,
  [string] $OutputRoot
)

$ErrorActionPreference = "Stop"

function Get-LogField([string] $line, [string] $name) {
  $prefix = "$name="
  foreach ($token in $line.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
    if ($token.StartsWith($prefix, [StringComparison]::Ordinal)) {
      return $token.Substring($prefix.Length).Trim('"')
    }
  }

  return $null
}

function Get-RequiredLongField([string] $line, [string] $name) {
  $value = Get-LogField $line $name
  [long] $parsed = 0
  if ($null -eq $value -or -not [long]::TryParse($value, [ref] $parsed)) {
    throw "Missing or invalid '$name' in log line: $line"
  }

  return $parsed
}

function Get-Median([double[]] $values) {
  if ($values.Count -eq 0) {
    throw "Cannot calculate a median from no values."
  }

  $ordered = @($values | Sort-Object)
  return $ordered[[int][math]::Floor($ordered.Count / 2)]
}

function Get-Percentile95([double[]] $values) {
  if ($values.Count -eq 0) {
    return 0
  }

  $ordered = @($values | Sort-Object)
  return $ordered[[int][math]::Ceiling(($ordered.Count - 1) * 0.95)]
}

function Get-CompletedRun([string] $runtimeLog) {
  $lines = @(Get-Content -LiteralPath $runtimeLog)
  $completed = $lines |
    Where-Object { $_ -match '(^|\s)cat=run(\s|$)' -and $_ -match '(^|\s)evt=completed(\s|$)' -and $_ -match '(^|\s)status=completed(\s|$)' } |
    Select-Object -Last 1
  if ($null -eq $completed) {
    throw "Runtime log has no successful completed event: $runtimeLog"
  }

  $samples = @($lines | Where-Object { $_ -match '(^|\s)cat=run(\s|$)' -and $_ -match '(^|\s)evt=sampled(\s|$)' })
  if ($samples.Count -eq 0) {
    throw "Runtime log has no sampled events: $runtimeLog"
  }

  $poolOperations = @($lines | Where-Object { $_ -match '(^|\s)op=pool(\s|$)' -and $_ -match '(^|\s)evt=summary(\s|$)' })
  $queueWaits = @($poolOperations | ForEach-Object {
    $raw = Get-LogField $_ 'queueWaitMs'
    [double] $value = 0
    if ($null -ne $raw -and [double]::TryParse($raw, [ref] $value)) { $value }
  })

  return [ordered]@{
    elapsedMs = Get-RequiredLongField $completed 'elapsedMs'
    nodeCount = Get-RequiredLongField $completed 'nodes'
    edgeCount = Get-RequiredLongField $completed 'edges'
    seedMarks = Get-RequiredLongField $completed 'seedMarks'
    propagatedMarks = Get-RequiredLongField $completed 'propagatedMarks'
    liftedMarks = Get-RequiredLongField $completed 'liftedMarks'
    decisions = Get-RequiredLongField $completed 'decisions'
    edits = Get-RequiredLongField $completed 'edits'
    diagnostics = Get-RequiredLongField $completed 'diags'
    workingSetPeakBytes = [long](($samples | ForEach-Object { Get-RequiredLongField $_ 'wsBytes' } | Measure-Object -Maximum).Maximum)
    allocationPeakBytes = [long](($samples | ForEach-Object { Get-RequiredLongField $_ 'allocBytes' } | Measure-Object -Maximum).Maximum)
    heapPeakBytes = [long](($samples | ForEach-Object { Get-RequiredLongField $_ 'heapBytes' } | Measure-Object -Maximum).Maximum)
    threadPoolThreadPeak = [long](($samples | ForEach-Object { Get-RequiredLongField $_ 'tpThreads' } | Measure-Object -Maximum).Maximum)
    threadPoolPendingPeak = [long](($samples | ForEach-Object { Get-RequiredLongField $_ 'tpPending' } | Measure-Object -Maximum).Maximum)
    queueWaitMedianMs = Get-Median $queueWaits
    queueWaitP95Ms = Get-Percentile95 $queueWaits
    agingPromotionCount = [long](@($poolOperations | Where-Object { (Get-LogField $_ 'admissionReason') -eq 'AgingPromotion' }).Count)
    weightedTurnCount = [long](@($poolOperations | Where-Object { (Get-LogField $_ 'admissionReason') -eq 'WeightedTurn' }).Count)
  }
}

function Get-SnapshotKey([System.Collections.IDictionary] $metrics) {
  return "nodes=$($metrics.nodeCount)|edges=$($metrics.edgeCount)|seed=$($metrics.seedMarks)|propagated=$($metrics.propagatedMarks)|lifted=$($metrics.liftedMarks)|decisions=$($metrics.decisions)|edits=$($metrics.edits)|diagnostics=$($metrics.diagnostics)"
}

function ConvertTo-YamlScalar([string] $value) {
  return "'" + $value.Replace("'", "''") + "'"
}

if (-not (Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
  throw "SourceFile does not exist: $SourceFile"
}

if ([IO.Path]::GetExtension($SourceFile) -ne '.cs') {
  throw "SourceFile must be a .cs file: $SourceFile"
}

$splitOptions = [StringSplitOptions]::RemoveEmptyEntries -bor [StringSplitOptions]::TrimEntries
$dopValues = @($Dop.Split(',', $splitOptions) | ForEach-Object {
  [int] $value = 0
  if (-not [int]::TryParse($_, [ref] $value) -or $value -lt 1) {
    throw "Dop must be a comma-separated list of positive integers."
  }

  $value
})
if ($dopValues.Count -eq 0) {
  throw "Dop must contain one or more positive values."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$env:DOTNET_CLI_HOME = $repoRoot
$resolvedSource = (Resolve-Path -LiteralPath $SourceFile).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $runId = "{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
  $OutputRoot = Join-Path $repoRoot "Build\PerformanceResults\$runId"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$logsRoot = Join-Path $OutputRoot 'logs'
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
$project = Join-Path $repoRoot 'src\NLISSN\NLISSN.csproj'
$configurationPath = Join-Path $OutputRoot 'nlissn.yml'
$allSamples = @()
$baselineSnapshot = $null

foreach ($currentDop in @($dopValues | Sort-Object -Unique)) {
  for ($run = 1; $run -le ($WarmupCount + $MeasurementCount); $run++) {
    $phase = if ($run -le $WarmupCount) { 'warmup' } else { 'measurement' }
    $phaseIndex = if ($phase -eq 'warmup') { $run } else { $run - $WarmupCount }
    $runId = "dop-{0}-{1}-{2:D2}-{3}" -f $currentDop, $phase, $phaseIndex, ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $yamlSourcePath = $resolvedSource.Replace('\', '/')
    $yamlTargetName = ConvertTo-YamlScalar $TargetName
    $yamlInputPath = ConvertTo-YamlScalar $yamlSourcePath
    @"
schemaVersion: 3
tool: nlissn
runId: $runId
input:
  path: $yamlInputPath
analysis:
  targetName: $yamlTargetName
execution:
  writeBack: false
  skipRewrite: true
  directoryMaxDegreeOfParallelism: $currentDop
  cpgMaxDegreeOfParallelism: $currentDop
  groupMaxDegreeOfParallelism: $currentDop
  helperMaxDegreeOfParallelism: $currentDop
  replayMaxDegreeOfParallelism: $currentDop
  maxConcurrentOperations: $currentDop
artifacts:
  root: logs
  diff: { enabled: false }
  runtimeLog: { enabled: true }
  evidence: { enabled: false }
  rewritePlan: { mode: none }
  analysisLog: { enabled: false }
logging:
  profile: benchmark
  level: info
  categories: []
  events: []
  view: benchmark
"@ | Set-Content -LiteralPath $configurationPath -Encoding utf8
    $logPath = Join-Path $logsRoot ("$runId\RuntimeLog\runtime.log")
    Push-Location $OutputRoot
    try {
      & dotnet run --no-build --project $project
    }
    finally {
      Pop-Location
    }
    if ($LASTEXITCODE -ne 0) {
      throw "DOP $currentDop $phase $phaseIndex failed with exit code $LASTEXITCODE."
    }

    $metrics = Get-CompletedRun $logPath
    $snapshot = Get-SnapshotKey $metrics
    if ($null -eq $baselineSnapshot) {
      $baselineSnapshot = $snapshot
    }
    elseif ($baselineSnapshot -ne $snapshot) {
      throw "DOP $currentDop $phase $phaseIndex has a semantic snapshot different from the DOP 1 baseline."
    }

    $allSamples += [ordered]@{
      dop = $currentDop
      phase = $phase
      run = $phaseIndex
      log = [IO.Path]::GetRelativePath($OutputRoot, $logPath).Replace('\', '/')
      snapshot = $snapshot
      metrics = $metrics
    }
  }
}

$rows = foreach ($currentDop in @($dopValues | Sort-Object -Unique)) {
  $measurements = @($allSamples | Where-Object { $_.dop -eq $currentDop -and $_.phase -eq 'measurement' })
  if ($measurements.Count -ne $MeasurementCount) {
    throw "DOP $currentDop has $($measurements.Count) measurements; expected $MeasurementCount."
  }

  [ordered]@{
    dop = $currentDop
    elapsedMedianMs = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.elapsedMs })
    workingSetPeakMedianBytes = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.workingSetPeakBytes })
    allocationPeakMedianBytes = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.allocationPeakBytes })
    heapPeakMedianBytes = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.heapPeakBytes })
    threadPoolThreadPeakMedian = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.threadPoolThreadPeak })
    threadPoolPendingPeakMedian = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.threadPoolPendingPeak })
    queueWaitMedianMs = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.queueWaitMedianMs })
    queueWaitP95Ms = Get-Median @($measurements | ForEach-Object { [double]$_.metrics.queueWaitP95Ms })
    weightedTurnCount = [long](($measurements | ForEach-Object { $_.metrics.weightedTurnCount } | Measure-Object -Sum).Sum)
    agingPromotionCount = [long](($measurements | ForEach-Object { $_.metrics.agingPromotionCount } | Measure-Object -Sum).Sum)
  }
}

$metadata = [ordered]@{
  sourceFile = $resolvedSource
  sourceSha256 = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash.ToLowerInvariant()
  targetName = $TargetName
  gitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
  sdk = (& dotnet --version).Trim()
  warmupCount = $WarmupCount
  measurementCount = $MeasurementCount
  generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
}
$summary = [ordered]@{ metadata = $metadata; snapshot = $baselineSnapshot; rows = @($rows); samples = $allSamples }
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot 'summary.json') -Encoding utf8
$rows | ConvertTo-Csv -NoTypeInformation | Set-Content -LiteralPath (Join-Path $OutputRoot 'summary.csv') -Encoding utf8

Write-Host "[Run-ConcurrencyPoolPerformance] evidence: $OutputRoot"
