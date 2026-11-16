$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\LumiTasks.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'Missing project file' }
$work = Join-Path $env:TEMP ('LumiTasksSourceBuild_' + [guid]::NewGuid().ToString('N'))
if ($work -cmatch '[^\x00-\x7F]') { throw 'TEMP path must contain ASCII characters only' }
$stagedSrc = Join-Path $work 'src'
$publish = Join-Path $work 'publish'
try {
  New-Item -ItemType Directory -Path $work,$publish -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $root 'src') -Destination $stagedSrc -Recurse -Force
  foreach ($sub in @('obj','bin')) {
    $stale=Join-Path $stagedSrc $sub
    if(Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
  }
  $stagedProject = Join-Path $stagedSrc 'LumiTasks.csproj'
  & dotnet restore $stagedProject --runtime win-x64
  if ($LASTEXITCODE -ne 0) { throw 'RESTORE_FAILED' }
  & dotnet publish $stagedProject -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publish
  if ($LASTEXITCODE -ne 0) { throw 'BUILD_FAILED' }
  $output = Join-Path $root 'publish'
  New-Item -ItemType Directory -Path $output -Force | Out-Null
  Get-ChildItem -LiteralPath $publish -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output $_.Name) -Force }
  Write-Host "BUILD_OK: $output"
  Write-Host 'No installed LumiTasks files were touched.'
} finally {
  Remove-Item -LiteralPath $work -Force -Recurse -ErrorAction SilentlyContinue
}
