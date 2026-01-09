# PowerShell module wrapper for applocate
# Exposes: Invoke-AppLocate, Find-App, Get-AppLocateJson
# PSScriptAnalyzer: PSAvoidAssignmentToAutomaticVariable warnings were reported referencing lines with no $args assignments.
# Suppressing rule at file scope as we only *read* remaining arguments via a custom parameter array.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidAssignmentToAutomaticVariable','')]

$script:ApplocatePath = $null

function Set-AppLocatePath {
    <#
    .SYNOPSIS
        Sets the path to the applocate executable.
    .DESCRIPTION
        Configures the module to use a specific applocate.exe path. Use this if the
        executable is not in the module directory or artifacts folder.
    .PARAMETER Path
        Full path to applocate.exe
    .EXAMPLE
        Set-AppLocatePath "C:\tools\applocate.exe"
    #>
    param([string]$Path)
    if(-not (Test-Path $Path)) { throw "Path not found: $Path" }
    $script:ApplocatePath = (Resolve-Path $Path).Path
}

function Get-AppLocatePath {
    <#
    .SYNOPSIS
        Returns the path to the applocate executable.
    .DESCRIPTION
        Returns the configured path, or auto-discovers it from:
        1. Module directory (bundled with PSGallery package)
        2. artifacts/win-x64 (development layout)
        3. System PATH
    #>
    if(-not $script:ApplocatePath) {
        $moduleRoot = Split-Path -Parent $PSCommandPath
        
        # 1. Check module directory (PSGallery bundle)
        $candidate = Join-Path $moduleRoot 'applocate.exe'
        if(Test-Path $candidate){ 
            $script:ApplocatePath = (Resolve-Path $candidate).Path 
            return $script:ApplocatePath
        }
        
        # 2. Check artifacts/win-x64 (development layout)
        $candidate = Join-Path $moduleRoot 'artifacts/win-x64/applocate.exe'
        if(Test-Path $candidate){ 
            $script:ApplocatePath = (Resolve-Path $candidate).Path 
            return $script:ApplocatePath
        }
        
        # 3. Check system PATH
        $inPath = Get-Command 'applocate' -ErrorAction SilentlyContinue
        if($inPath){ 
            $script:ApplocatePath = $inPath.Source 
            return $script:ApplocatePath
        }
    }
    if(-not $script:ApplocatePath) { 
        throw "applocate executable not found. Install via WinGet (winget install AppLocate.AppLocate), download from GitHub releases, or use Set-AppLocatePath." 
    }
    return $script:ApplocatePath
}

function Invoke-AppLocate {
    <#
    .SYNOPSIS
        Invokes the applocate CLI with the specified query and options.
    .DESCRIPTION
        Runs applocate.exe with any combination of arguments. Use -Json or -Csv
        to get structured output, or pass CLI flags directly.
    .PARAMETER QueryAndOptions
        The application query and any additional CLI options (e.g., --all, --evidence)
    .PARAMETER Json
        Output results as JSON
    .PARAMETER Csv
        Output results as CSV
    .EXAMPLE
        Invoke-AppLocate "chrome"
    .EXAMPLE
        Invoke-AppLocate "vscode" "--all" "--evidence" -Json
    .EXAMPLE
        Invoke-AppLocate "node" "--running" "--exe"
    #>
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
    if($p.ExitCode -ne 0 -and $p.ExitCode -ne 1){ Write-Error "applocate exited $($p.ExitCode)" }
    return $out
}

function Get-AppLocateJson {
    <#
    .SYNOPSIS
        Searches for an application and returns results as PowerShell objects.
    .DESCRIPTION
        Queries applocate with JSON output and parses the result into PowerShell objects.
        Supports filtering by confidence threshold and result limit.
    .PARAMETER Query
        The application name or alias to search for
    .PARAMETER ConfidenceMin
        Minimum confidence score (0-1) to include in results
    .PARAMETER Limit
        Maximum number of results to return
    .EXAMPLE
        Get-AppLocateJson -Query "code"
    .EXAMPLE
        Get-AppLocateJson -Query "chrome" -ConfidenceMin 0.7 -Limit 5
    .OUTPUTS
        PSCustomObject[] - Array of application hit objects with path, confidence, type, etc.
    #>
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

function Find-App {
    <#
    .SYNOPSIS
        Quick search for an application (alias for Get-AppLocateJson).
    .DESCRIPTION
        Convenience function that wraps Get-AppLocateJson for simple queries.
    .PARAMETER Query
        The application name or alias to search for
    .EXAMPLE
        Find-App "notepad"
    .EXAMPLE
        Find-App "git" | Select-Object path, confidence
    #>
    [CmdletBinding()] param(
        [Parameter(Mandatory)][string]$Query
    )
    Get-AppLocateJson -Query $Query
}

Export-ModuleMember -Function Set-AppLocatePath,Get-AppLocatePath,Invoke-AppLocate,Get-AppLocateJson,Find-App
