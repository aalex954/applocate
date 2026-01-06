# PowerShell module wrapper for applocate
# Exposes: Invoke-AppLocate, Find-App, Get-AppLocateJson
# PSScriptAnalyzer: PSAvoidAssignmentToAutomaticVariable warnings were reported referencing lines with no $args assignments.
# Suppressing rule at file scope as we only *read* remaining arguments via a custom parameter array.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidAssignmentToAutomaticVariable','')]

$script:ApplocatePath = $null

function Set-AppLocatePath {
    param([string]$Path)
    if(-not (Test-Path $Path)) { throw "Path not found: $Path" }
    $script:ApplocatePath = (Resolve-Path $Path).Path
}

function Get-AppLocatePath {
    if(-not $script:ApplocatePath) {
        # Try locate relative to module root (artifacts win-x64 default)
        $moduleRoot = Split-Path -Parent $PSCommandPath
        $candidate = Join-Path $moduleRoot 'artifacts/win-x64/applocate.exe'
        if(Test-Path $candidate){ $script:ApplocatePath = (Resolve-Path $candidate).Path }
    }
    if(-not $script:ApplocatePath) { throw "applocate executable path not set. Use Set-AppLocatePath." }
    return $script:ApplocatePath
}

function Invoke-AppLocate {
    [CmdletBinding()] param(
        [Parameter(Mandatory, Position=0, ValueFromRemainingArguments)] [string[]]$QueryAndOptions,
        [switch]$Json,
        [switch]$Csv
    )
    $exe = Get-AppLocatePath
    $cliArgs = @()
    if($Json){ $cliArgs += '--json' }
    elseif($Csv){ $cliArgs += '--csv' }
    $cliArgs += $QueryAndOptions
    # Build a properly escaped argument string for ProcessStartInfo
    $argString = ($cliArgs | ForEach-Object {
        if ($_ -match '\s') { "`"$_`"" } else { $_ }
    }) -join ' '
    $psi = New-Object System.Diagnostics.ProcessStartInfo -Property @{ FileName = $exe; Arguments = $argString; RedirectStandardOutput = $true; RedirectStandardError = $true; UseShellExecute = $false }
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    if($p.ExitCode -ne 0){ Write-Error "applocate exited $($p.ExitCode)" }
    return $out
}

function Get-AppLocateJson {
    [CmdletBinding()] param(
        [Parameter(Mandatory)][string]$Query,
        [double]$ConfidenceMin,
        [int]$Limit
    )
    $cliArgs = @($Query,'--json')
    if($PSBoundParameters.ContainsKey('ConfidenceMin')){ $cliArgs += @('--confidence-min', $ConfidenceMin) }
    if($PSBoundParameters.ContainsKey('Limit')){ $cliArgs += @('--limit', $Limit) }
    $raw = Invoke-AppLocate -QueryAndOptions $cliArgs -Json
    try { return $raw | ConvertFrom-Json } catch { Write-Error "Failed to parse JSON: $_"; return $raw }
}

function Find-App { [CmdletBinding()] param([Parameter(Mandatory)][string]$Query) Get-AppLocateJson -Query $Query }

Export-ModuleMember -Function Set-AppLocatePath,Get-AppLocatePath,Invoke-AppLocate,Get-AppLocateJson,Find-App
