$report = @()
# Language lowercase dir count
$languageBadDirs = Get-ChildItem -Path 'src/Logic/Analysis/Engine/Language' -Directory -Recurse | Where-Object {
  (($_.FullName.Substring($PWD.Path.Length + 1) -split '[\\/]') | Select-Object -Skip 5 | Where-Object { $_ -cnotmatch '^[A-Z][A-Za-z0-9]*$' }).Count -gt 0
}
$report += [PSCustomObject]@{ Path='src/Logic/Analysis/Engine/Language'; Files=(Get-ChildItem 'src/Logic/Analysis/Engine/Language' -Recurse -File -Filter *.cs).Count; Signal="lowercase/style-drift dirs=$($languageBadDirs.Count)" }
$mapperDot = Get-ChildItem 'src/Application/Mappers' -File -Filter *.cs | Where-Object { $_.BaseName -like '*.*' }
$report += [PSCustomObject]@{ Path='src/Application/Mappers'; Files=(Get-ChildItem 'src/Application/Mappers' -File -Filter *.cs).Count; Signal="dotted partial files=$($mapperDot.Count)" }
$report += [PSCustomObject]@{ Path='src/Application/Contracts'; Files=(Get-ChildItem 'src/Application/Contracts' -Recurse -File -Filter *.cs).Count; Signal='aggregated enum/dto files and Contract* prefix cluster' }
$report += [PSCustomObject]@{ Path='src/Domain/Analysis/Engine/Query'; Files=(Get-ChildItem 'src/Domain/Analysis/Engine/Query' -File -Filter *.cs).Count; Signal='generic/wrapper names: Engine, TaskCreator, TaskSolver + Query* duplicates' }
$report += [PSCustomObject]@{ Path='src/Logic/Analysis/Engine/Passes/ControlFlow'; Files=(Get-ChildItem 'src/Logic/Analysis/Engine/Passes/ControlFlow' -Recurse -File -Filter *.cs).Count; Signal='typo CodePenceGraph + Cpg/Cfg abbreviation cluster' }
$report += [PSCustomObject]@{ Path='src/Infrastructure/Analysis/Engine'; Files=(Get-ChildItem 'src/Infrastructure/Analysis/Engine' -Recurse -File -Filter *.cs).Count; Signal='X2Cpg/Cfg abbreviation-heavy infrastructure terms' }
$report | Format-Table -AutoSize
