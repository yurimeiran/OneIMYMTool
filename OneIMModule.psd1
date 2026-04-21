@{
    ModuleVersion     = '1.0.0'
    GUID              = 'a3f2e1d0-4b5c-4f6e-8a7b-9c0d1e2f3a4b'
    Author            = 'Administrator'
    CompanyName       = 'One Identity'
    Description       = 'PowerShell module for One Identity Manager — authentication, session management, and database compilation.'
    PowerShellVersion = '5.1'

    RootModule        = 'OneIMModule.psm1'

    FunctionsToExport = @()
    CmdletsToExport   = @(
        'Connect-OneIM'
        'Disconnect-OneIM'
        'Get-OneIMSession'
        'Invoke-OneIMCompile'
        'Get-OneIMEntity'
        'Invoke-OneIMMethod'
    )
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('OneIdentity', 'OneIM', 'IdentityManager')
            ProjectUri = ''
        }
    }
}
