$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "ROB_RUN_PING_BOOSTER.csproj"
dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o (Join-Path $PSScriptRoot "publish")
Write-Host ""
Write-Host "EXE created: $PSScriptRoot\publish\ROB RUN PING BOOSTER.exe" -ForegroundColor Green
