param(
  [switch]$Arm64,
  [switch]$X64,
  [string]$Configuration = "Release"
)

if(-not ($Arm64 -or $X64)) { $Arm64 = $true; $X64 = $true }

$ErrorActionPreference = 'Stop'
Write-Host "Publishing applocate ($Configuration)" -ForegroundColor Cyan

if($X64){
  dotnet publish ./src/AppLocate.Cli/AppLocate.Cli.csproj -c $Configuration -r win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true -p:SelfContained=true --no-self-contained:false -p:ReadyToRun=true -o ./artifacts/win-x64
}
if($Arm64){
  dotnet publish ./src/AppLocate.Cli/AppLocate.Cli.csproj -c $Configuration -r win-arm64 -p:PublishSingleFile=true -p:PublishTrimmed=true -p:SelfContained=true --no-self-contained:false -p:ReadyToRun=true -o ./artifacts/win-arm64
}

Write-Host "Done." -ForegroundColor Green
