@{
    RootModule = 'AppLocate.psm1'
    ModuleVersion = '0.1.5'
    GUID = '2b9b5a5f-2c2f-4c3d-9c9e-7f2a5b0c1b11'
    Author = 'aalex954'
    CompanyName = 'AppLocate'
    Copyright = '(c) 2025-2026 AppLocate Contributors. MIT License.'
    Description = 'Find any app on Windows instantly. PowerShell wrapper for the applocate CLI tool that searches registry, Start Menu, running processes, package managers (Scoop, Chocolatey, WinGet), and more. Returns ranked results with confidence scores in JSON/CSV/text format.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @('Set-AppLocatePath','Get-AppLocatePath','Invoke-AppLocate','Get-AppLocateJson','Find-App')
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('Windows', 'Application', 'Discovery', 'Registry', 'Scoop', 'Chocolatey', 'WinGet', 'CLI', 'Search', 'Locate')
            LicenseUri = 'https://github.com/aalex954/applocate/blob/main/LICENSE'
            ProjectUri = 'https://github.com/aalex954/applocate'
            IconUri = 'https://raw.githubusercontent.com/aalex954/applocate/main/assets/logo.svg'
            ReleaseNotes = 'https://github.com/aalex954/applocate/releases'
            Prerelease = ''
        }
    }
}
