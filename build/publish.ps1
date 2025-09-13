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

# Copy PowerShell module files to artifacts root for distribution/import convenience
$moduleDest = Join-Path (Resolve-Path './artifacts').Path '.'
foreach($f in 'AppLocate.psm1','AppLocate.psd1'){
  $src = Join-Path (Resolve-Path '.').Path $f
  if(Test-Path $src){ Copy-Item $src $moduleDest -Force }
}
Write-Host "Module files copied to artifacts" -ForegroundColor Yellow
