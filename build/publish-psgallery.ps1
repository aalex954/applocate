<#
.SYNOPSIS
    Publishes the AppLocate PowerShell module to PowerShell Gallery.
.DESCRIPTION
    Builds a self-contained module package with the applocate.exe bundled,
    then publishes to PowerShell Gallery using the provided API key.
.PARAMETER ApiKey
    PowerShell Gallery API key (from https://www.powershellgallery.com/account/apikeys)
.PARAMETER Version
    Optional version override (defaults to version in psd1)
.PARAMETER DryRun
    Build and stage the module, but don't actually publish
.PARAMETER SkipBuild
    Skip the dotnet publish step (use existing artifacts)
.EXAMPLE
    ./build/publish-psgallery.ps1 -ApiKey $env:PSGALLERY_API_KEY
.EXAMPLE
    ./build/publish-psgallery.ps1 -DryRun
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$ApiKey,

    [Parameter(Mandatory = $false)]
    [string]$Version,

    [switch]$DryRun,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleName = 'AppLocate'
$stagingDir = Join-Path $repoRoot "artifacts/psgallery/$moduleName"

Write-Host "=== AppLocate PowerShell Gallery Publisher ===" -ForegroundColor Cyan
Write-Host "Repository root: $repoRoot"
Write-Host "Staging directory: $stagingDir"

# Clean and create staging directory
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Build the CLI if not skipping
if (-not $SkipBuild) {
    Write-Host "`n[1/5] Building applocate.exe (win-x64)..." -ForegroundColor Yellow
    $publishArgs = @(
        'publish'
        "$repoRoot/src/AppLocate.Cli/AppLocate.Cli.csproj"
        '-c', 'Release'
        '-r', 'win-x64'
        '-p:PublishSingleFile=true'
        '-p:SelfContained=true'
        '-p:EnableCompressionInSingleFile=true'
        '-p:PublishReadyToRun=true'
        '-o', "$repoRoot/artifacts/win-x64"
    )
    if ($Version) {
        $publishArgs += "-p:Version=$Version"
    }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "`n[1/5] Skipping build (using existing artifacts)..." -ForegroundColor Yellow
}

# Verify exe exists
$exePath = Join-Path $repoRoot 'artifacts/win-x64/applocate.exe'
if (-not (Test-Path $exePath)) {
    throw "applocate.exe not found at $exePath. Run without -SkipBuild or build first."
}
Write-Host "Found applocate.exe: $exePath" -ForegroundColor Green

# Copy module files
Write-Host "`n[2/5] Staging module files..." -ForegroundColor Yellow
Copy-Item "$repoRoot/AppLocate.psd1" $stagingDir -Force
Copy-Item "$repoRoot/AppLocate.psm1" $stagingDir -Force
Copy-Item $exePath $stagingDir -Force
Copy-Item "$repoRoot/LICENSE" $stagingDir -Force

# Update version in staged manifest if specified
if ($Version) {
    Write-Host "`n[3/5] Updating module version to $Version..." -ForegroundColor Yellow
    $psd1Path = Join-Path $stagingDir 'AppLocate.psd1'
    $content = Get-Content $psd1Path -Raw
    $content = $content -replace "ModuleVersion = '[^']*'", "ModuleVersion = '$Version'"
    Set-Content $psd1Path -Value $content -NoNewline
} else {
    Write-Host "`n[3/5] Using version from manifest..." -ForegroundColor Yellow
}

# Test the module can be imported
Write-Host "`n[4/5] Testing module import..." -ForegroundColor Yellow
try {
    Import-Module $stagingDir -Force -ErrorAction Stop
    $commands = Get-Command -Module $moduleName
    Write-Host "  Exported commands: $($commands.Name -join ', ')" -ForegroundColor Green
    
    # Quick sanity check
    $exeCheck = Get-AppLocatePath
    Write-Host "  Exe path resolved: $exeCheck" -ForegroundColor Green
    Remove-Module $moduleName -Force
} catch {
    throw "Module import test failed: $_"
}

# List staged files
Write-Host "`nStaged files:" -ForegroundColor Cyan
Get-ChildItem $stagingDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { "{0:N1} MB" -f ($_.Length / 1MB) } else { "{0:N0} KB" -f ($_.Length / 1KB) }
    Write-Host "  $($_.Name) ($size)"
}

# Publish
Write-Host "`n[5/5] Publishing to PowerShell Gallery..." -ForegroundColor Yellow

if ($DryRun -or -not $ApiKey) {
    Write-Host "`n  [DRY RUN] Would publish module from: $stagingDir" -ForegroundColor Magenta
    Write-Host "  To publish for real, provide -ApiKey parameter" -ForegroundColor Magenta
    
    # Validate manifest
    Write-Host "`n  Validating manifest..." -ForegroundColor Yellow
    $manifest = Test-ModuleManifest -Path (Join-Path $stagingDir 'AppLocate.psd1')
    Write-Host "  Module: $($manifest.Name) v$($manifest.Version)" -ForegroundColor Green
    Write-Host "  Description: $($manifest.Description)" -ForegroundColor Green
    Write-Host "  Tags: $($manifest.PrivateData.PSData.Tags -join ', ')" -ForegroundColor Green
    
    Write-Host "`n=== Dry run complete ===" -ForegroundColor Cyan
    exit 0
}

Publish-Module -Path $stagingDir -NuGetApiKey $ApiKey -Verbose
Write-Host "`n=== Successfully published to PowerShell Gallery! ===" -ForegroundColor Green
Write-Host "View at: https://www.powershellgallery.com/packages/AppLocate" -ForegroundColor Cyan
