$candidates = @(
  'src/Logic/Analysis/Engine/Language',
  'src/Domain/Analysis/Engine/Query',
  'src/Application/Mappers',
  'src/Application/Contracts',
  'src/Logic/Analysis/Engine/Passes/ControlFlow',
  'src/Infrastructure/Analysis/Engine'
)
foreach ($path in $candidates) {
  $fileCount = (Get-ChildItem -Path $path -Recurse -File -Filter *.cs | Measure-Object).Count
  $dirCount = (Get-ChildItem -Path $path -Recurse -Directory | Measure-Object).Count
  Write-Output "$path | files=$fileCount | dirs=$dirCount"
}
