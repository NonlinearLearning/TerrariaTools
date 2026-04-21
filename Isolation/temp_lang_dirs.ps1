$base = 'src/Logic/Analysis/Engine/Language'
Get-ChildItem -Path $base -Directory -Recurse | ForEach-Object {
  $relative = $_.FullName.Substring($PWD.Path.Length + 1)
  $segments = ($relative -split '[\\/]') | Select-Object -Skip 5
  $bad = $segments | Where-Object { $_ -cnotmatch '^[A-Z][A-Za-z0-9]*$' }
  if ($bad) {
    [PSCustomObject]@{ Path=$relative; BadSegments=($bad -join ' / ') }
  }
} | Format-Table -AutoSize
