param(
  [Parameter(Mandatory = $true)]
  [string] $SourceRoot,
  [Parameter(Mandatory = $true)]
  [string] $OutputRoot,
  [string] $ConfigPath = (Join-Path $PSScriptRoot '..\docs\benchmarks\cpg-dop-small-fixture.json'),
  [switch] $CleanOutput
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$config = Get-Content -LiteralPath (Resolve-Path -LiteralPath $ConfigPath) -Raw | ConvertFrom-Json
$files = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
  $config.extensions -contains $_.Extension.ToLowerInvariant()
} | ForEach-Object {
  $relative = $_.FullName.Substring($source.Length).TrimStart('\', '/') -replace '\\', '/'
  [pscustomobject]@{
    FullPath = $_.FullName
    RelativePath = $relative
    Length = [int64]$_.Length
  }
})

if ($files.Count -lt ($config.largeFileCount + $config.smallFileCount)) {
  throw "Source root has $($files.Count) matching files; fixture requires $($config.largeFileCount + $config.smallFileCount)."
}

$largeMinBytes = if ($null -eq $config.largeMinBytes) { 0 } else { [int64]$config.largeMinBytes }
$eligibleLarge = @($files | Where-Object {
  $_.Length -ge $largeMinBytes -and $_.Length -le $config.largeMaxBytes
})
if ($eligibleLarge.Count -lt $config.largeFileCount) {
  throw "Source root has $($eligibleLarge.Count) matching files within [$largeMinBytes, $($config.largeMaxBytes)] bytes; fixture requires $($config.largeFileCount)."
}

$large = @($eligibleLarge | Sort-Object @{Expression = 'Length'; Descending = $true}, RelativePath | Select-Object -First $config.largeFileCount)
$largePaths = @($large | ForEach-Object RelativePath)
$small = @($files | Where-Object { $largePaths -notcontains $_.RelativePath } | Sort-Object Length, RelativePath | Select-Object -First $config.smallFileCount)
$selected = @($large + $small)

if ($CleanOutput -and (Test-Path -LiteralPath $OutputRoot)) {
  $resolvedOutput = (Resolve-Path -LiteralPath $OutputRoot).Path
  if ($resolvedOutput -eq $source -or $resolvedOutput -eq [IO.Path]::GetPathRoot($resolvedOutput)) {
    throw "Refusing to clean source or filesystem root: $resolvedOutput"
  }
  Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$manifestFiles = foreach ($file in $selected) {
  $destination = Join-Path $OutputRoot ($file.RelativePath -replace '/', '\')
  New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
  Copy-Item -LiteralPath $file.FullPath -Destination $destination -Force
  $hash = (Get-FileHash -LiteralPath $file.FullPath -Algorithm SHA256).Hash
  [pscustomobject]@{
    category = if ($largePaths -contains $file.RelativePath) { 'large' } else { 'small' }
    relativePath = $file.RelativePath
    bytes = $file.Length
    sha256 = $hash
  }
}

$manifest = [ordered]@{
  name = $config.name
  sourceRoot = $source
  generatedAtUtc = [DateTime]::UtcNow.ToString('O')
  selection = [ordered]@{ largeFileCount = $config.largeFileCount; largeMinBytes = $largeMinBytes; largeMaxBytes = $config.largeMaxBytes; smallFileCount = $config.smallFileCount; extensions = @($config.extensions); largeSelection = $config.largeSelection; smallSelection = $config.smallSelection; tieBreak = $config.tieBreak }
  fileCount = $manifestFiles.Count
  largeCount = @($manifestFiles | Where-Object category -eq 'large').Count
  smallCount = @($manifestFiles | Where-Object category -eq 'small').Count
  files = @($manifestFiles)
}
$manifestPath = Join-Path $OutputRoot 'manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Output ([pscustomobject]@{
  FixtureRoot = $OutputRoot
  ManifestPath = $manifestPath
  FileCount = $manifest.fileCount
  LargeCount = $manifest.largeCount
  SmallCount = $manifest.smallCount
  Bytes = ($manifestFiles | Measure-Object -Property bytes -Sum).Sum
} | Format-List | Out-String)
