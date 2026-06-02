<#
.SYNOPSIS
    Smoke tests for the Optimizer PowerShell module.

.DESCRIPTION
    Verifies the module loads cleanly and exports exactly the expected functions.
    Does NOT require a live server — all checks are structural only.

    Run with:
        pwsh -NonInteractive -File Optimizer.Tests.ps1

    Or with Pester (if installed):
        Invoke-Pester ./Optimizer.Tests.ps1 -Output Detailed
#>

$ModuleManifest = Join-Path $PSScriptRoot '..\Optimizer.psd1'
$ModuleName     = 'Optimizer'

$ExpectedFunctions = @(
    'Connect-Optimizer',
    'Get-OptimizerStatus',
    'Get-OptimizerProfile',
    'Get-OptimizerPlugin',
    'Get-OptimizerSyncItem',
    'Register-OptimizerWebhook',
    'Get-OptimizerWebhook',
    'Unregister-OptimizerWebhook'
)

# ── Pester tests (if Pester is available) ────────────────────────────────────

# Pester v5+ required for modern Should syntax (-Not, -BeNullOrEmpty, etc.)
$pesterAvailable = $null -ne (Get-Module -ListAvailable Pester -ErrorAction SilentlyContinue |
    Where-Object { $_.Version.Major -ge 5 } | Select-Object -First 1)

if ($pesterAvailable) {

    Describe 'Optimizer PowerShell Module' {

        BeforeAll {
            Import-Module $ModuleManifest -Force -ErrorAction Stop
        }

        AfterAll {
            Remove-Module $ModuleName -ErrorAction SilentlyContinue
        }

        It 'Module manifest is valid' {
            $manifest = Test-ModuleManifest -Path $ModuleManifest
            $manifest | Should -Not -BeNullOrEmpty
            $manifest.Version.ToString() | Should -Be '1.0.0'
        }

        It 'Module imports without errors' {
            { Import-Module $ModuleManifest -Force -ErrorAction Stop } | Should -Not -Throw
        }

        foreach ($fn in $ExpectedFunctions) {
            It "Exports function '$fn'" {
                $cmd = Get-Command -Module $ModuleName -Name $fn -ErrorAction SilentlyContinue
                $cmd | Should -Not -BeNullOrEmpty
            }
        }

        It 'Does not export private helper functions' {
            $exported = Get-Command -Module $ModuleName | Select-Object -ExpandProperty Name
            $exported | Should -Not -Contain '_EnsureConnected'
            $exported | Should -Not -Contain '_Get'
            $exported | Should -Not -Contain '_Post'
            $exported | Should -Not -Contain '_Delete'
        }

        It 'Connect-Optimizer has mandatory ServerUrl parameter' {
            $cmd = Get-Command Connect-Optimizer
            $param = $cmd.Parameters['ServerUrl']
            $param | Should -Not -BeNullOrEmpty
            $param.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] -and $_.Mandatory } | Should -Not -BeNullOrEmpty
        }

        It 'Connect-Optimizer has mandatory ApiKey parameter' {
            $cmd = Get-Command Connect-Optimizer
            $param = $cmd.Parameters['ApiKey']
            $param | Should -Not -BeNullOrEmpty
            $param.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] -and $_.Mandatory } | Should -Not -BeNullOrEmpty
        }
    }

}
else {
    # ── Fallback: plain script smoke test (no Pester dependency) ─────────────

    Write-Host "Running Optimizer module smoke tests (no Pester — using plain assertions)..." -ForegroundColor Cyan

    $failed = 0

    function Assert-True ([bool]$Condition, [string]$Message) {
        if ($Condition) {
            Write-Host "  PASS  $Message" -ForegroundColor Green
        } else {
            Write-Host "  FAIL  $Message" -ForegroundColor Red
            $script:failed++
        }
    }

    # Test 1: manifest is valid
    try {
        $manifest = Test-ModuleManifest -Path $ModuleManifest -ErrorAction Stop
        Assert-True ($null -ne $manifest) "Module manifest is valid"
        Assert-True ($manifest.Version.ToString() -eq '1.0.0') "Manifest version is 1.0.0"
    }
    catch {
        Assert-True $false "Module manifest is valid — $_"
    }

    # Test 2: module imports without errors
    try {
        Import-Module $ModuleManifest -Force -ErrorAction Stop
        Assert-True $true "Module imports without errors"
    }
    catch {
        Assert-True $false "Module imports without errors — $_"
    }

    # Test 3: all expected functions are exported
    $exported = (Get-Command -Module $ModuleName -ErrorAction SilentlyContinue).Name
    foreach ($fn in $ExpectedFunctions) {
        Assert-True ($exported -contains $fn) "Exports '$fn'"
    }

    # Test 4: private helpers are NOT exported
    Assert-True ($exported -notcontains '_Get')           "Does not export '_Get'"
    Assert-True ($exported -notcontains '_EnsureConnected') "Does not export '_EnsureConnected'"

    # Clean up
    Remove-Module $ModuleName -ErrorAction SilentlyContinue

    if ($failed -gt 0) {
        Write-Host "`n$failed test(s) FAILED." -ForegroundColor Red
        exit 1
    } else {
        Write-Host "`nAll tests passed." -ForegroundColor Green
        exit 0
    }
}
