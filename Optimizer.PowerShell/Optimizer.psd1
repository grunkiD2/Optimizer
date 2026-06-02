@{
    # Script module or binary module file associated with this manifest
    RootModule        = 'Optimizer.psm1'

    # Version number of this module.
    ModuleVersion     = '1.0.0'

    # Supported PSEditions
    CompatiblePSEditions = @('Core')

    # ID used to uniquely identify this module
    GUID              = 'a3f7c2d1-8e4b-4f90-b6a2-1c5d9e0f3b87'

    # Author of this module
    Author            = 'Optimizer Dev'

    # Company or vendor of this module
    CompanyName       = 'Optimizer Project'

    # Copyright statement for this module
    Copyright         = '(c) 2026 Optimizer Project. All rights reserved.'

    # Description of the functionality provided by this module
    Description       = 'PowerShell module for the Optimizer cloud server API. Wraps the REST API surface with idiomatic cmdlet-style functions.'

    # Minimum version of the PowerShell engine required by this module
    PowerShellVersion = '7.0'

    # Functions to export from this module
    FunctionsToExport = @(
        'Connect-Optimizer',
        'Get-OptimizerStatus',
        'Get-OptimizerProfile',
        'Get-OptimizerPlugin',
        'Get-OptimizerSyncItem',
        'Register-OptimizerWebhook',
        'Get-OptimizerWebhook',
        'Unregister-OptimizerWebhook'
    )

    # Cmdlets to export from this module
    CmdletsToExport   = @()

    # Aliases to export from this module
    AliasesToExport   = @()

    # Private data to pass to the module specified in RootModule/ModuleToProcess
    PrivateData = @{
        PSData = @{
            Tags         = @('Optimizer', 'API', 'SystemOptimization', 'Automation')
            ProjectUri   = 'https://github.com/optimizer-project/optimizer'
            ReleaseNotes = 'Initial release — full cloud API surface: connect, status, profiles, plugins, sync, webhooks.'
        }
    }
}
