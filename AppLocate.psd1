@{
    RootModule = 'AppLocate.psm1'
    ModuleVersion = '0.1.5'
    GUID = '2b9b5a5f-2c2f-4c3d-9c9e-7f2a5b0c1b11'
    Author = 'AppLocate'
    CompanyName = 'AppLocate'
    Copyright = '(c) AppLocate'
    Description = 'PowerShell wrapper for applocate CLI'
    PowerShellVersion = '5.1'
    FunctionsToExport = 'Set-AppLocatePath','Get-AppLocatePath','Invoke-AppLocate','Get-AppLocateJson','Find-App'
    CmdletsToExport = @()
    VariablesToExport = '*'
    AliasesToExport = @()
    PrivateData = @{}
}
