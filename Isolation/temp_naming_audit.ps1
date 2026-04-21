$roots = 'src/Application','src/Domain','src/Logic','src/Infrastructure'
$results = @()
foreach ($root in $roots) {
  Get-ChildItem -Path $root -Recurse -File -Filter *.cs | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $matches = [regex]::Matches($content, '(?m)^\s*(?:public|internal|private|protected|sealed|abstract|static|partial|file|readonly|unsafe\s+)*\s*(class|interface|record|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)')
    $types = @($matches | ForEach-Object { $_.Groups[2].Value })
    $base = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
    $relative = $_.FullName.Substring($PWD.Path.Length + 1)
    $dirSegments = Split-Path $relative -Parent | ForEach-Object { $_ -split '[\\/]' } | Where-Object { $_ }
    $lowerDirs = $dirSegments | Where-Object { $_ -cmatch '[a-z]' -and $_ -cnotmatch '^[A-Z][A-Za-z0-9]*$' }
    $fileCaseIssue = $base -cnotmatch '^[A-Z][A-Za-z0-9]*$'
    $typeMismatch = $false
    if ($types.Count -gt 0) {
      $typeMismatch = -not ($types -contains $base)
    }
    if ($lowerDirs.Count -gt 0 -or $fileCaseIssue -or $typeMismatch) {
      [PSCustomObject]@{
        Path = $relative
        Base = $base
        Types = ($types -join ', ')
        TypeMismatch = $typeMismatch
        LowerDirs = ($lowerDirs -join ', ')
        FileCaseIssue = $fileCaseIssue
      }
    }
  }
}
$results | Sort-Object Path | Format-Table -AutoSize
