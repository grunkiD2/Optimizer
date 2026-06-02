#Requires -Version 7.0
<#
.SYNOPSIS
    Optimizer PowerShell Module — wraps the Optimizer cloud server REST API.

.DESCRIPTION
    All cmdlets communicate with Optimizer.Server using an API key
    (X-Api-Key header). Call Connect-Optimizer first to store your
    server URL and key, then use the other cmdlets freely.

    Requires PowerShell 7+ for Invoke-RestMethod improvements.
#>

Set-StrictMode -Version Latest

# ── Module-scoped connection config ───────────────────────────────────────────

$script:_Config = @{
    ServerUrl = $null
    ApiKey    = $null
}

# ── Internal helpers ──────────────────────────────────────────────────────────

function _EnsureConnected {
    if (-not $script:_Config.ServerUrl -or -not $script:_Config.ApiKey) {
        throw "Not connected. Call Connect-Optimizer -ServerUrl <url> -ApiKey <key> first."
    }
}

function _Headers {
    return @{
        'X-Api-Key'    = $script:_Config.ApiKey
        'Content-Type' = 'application/json'
    }
}

function _ApiUrl ([string]$Path) {
    return "$($script:_Config.ServerUrl.TrimEnd('/'))$Path"
}

function _Get ([string]$Path) {
    _EnsureConnected
    try {
        return Invoke-RestMethod -Uri (_ApiUrl $Path) -Headers (_Headers) -Method GET
    }
    catch {
        $msg = if ($_.Exception.Response) {
            "HTTP $([int]$_.Exception.Response.StatusCode): $($_.Exception.Message)"
        } else {
            $_.Exception.Message
        }
        Write-Error "Optimizer API error on GET $Path : $msg"
        return $null
    }
}

function _Post ([string]$Path, [object]$Body = $null) {
    _EnsureConnected
    $params = @{
        Uri     = _ApiUrl $Path
        Headers = _Headers
        Method  = 'POST'
    }
    if ($null -ne $Body) {
        $params['Body']        = ($Body | ConvertTo-Json -Depth 10 -Compress)
        $params['ContentType'] = 'application/json'
    }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        $msg = if ($_.Exception.Response) {
            "HTTP $([int]$_.Exception.Response.StatusCode): $($_.Exception.Message)"
        } else {
            $_.Exception.Message
        }
        Write-Error "Optimizer API error on POST $Path : $msg"
        return $null
    }
}

function _Delete ([string]$Path) {
    _EnsureConnected
    try {
        Invoke-RestMethod -Uri (_ApiUrl $Path) -Headers (_Headers) -Method DELETE | Out-Null
        return $true
    }
    catch {
        $msg = if ($_.Exception.Response) {
            "HTTP $([int]$_.Exception.Response.StatusCode): $($_.Exception.Message)"
        } else {
            $_.Exception.Message
        }
        Write-Error "Optimizer API error on DELETE $Path : $msg"
        return $false
    }
}

# ── Public cmdlets ─────────────────────────────────────────────────────────────

<#
.SYNOPSIS
    Configure the connection to an Optimizer cloud server.

.DESCRIPTION
    Stores the server URL and API key in module scope so all subsequent
    cmdlets can use them without requiring repeated parameters.

    You can also set the environment variables OPTIMIZER_CLOUD_URL and
    OPTIMIZER_API_KEY before importing the module as an alternative.

.PARAMETER ServerUrl
    The base URL of the Optimizer.Server instance, e.g. https://my-optimizer.example.com

.PARAMETER ApiKey
    An API key created via Settings → Developer (or POST /api/keys with a JWT session).

.EXAMPLE
    Connect-Optimizer -ServerUrl 'http://localhost:5000' -ApiKey $env:OPTIMIZER_API_KEY

.EXAMPLE
    # Persist to environment for later sessions
    $env:OPTIMIZER_CLOUD_URL = 'http://localhost:5000'
    $env:OPTIMIZER_API_KEY   = 'opk_...'
    Connect-Optimizer -ServerUrl $env:OPTIMIZER_CLOUD_URL -ApiKey $env:OPTIMIZER_API_KEY
#>
function Connect-Optimizer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ServerUrl,

        [Parameter(Mandatory)]
        [string]$ApiKey
    )

    $script:_Config.ServerUrl = $ServerUrl.TrimEnd('/')
    $script:_Config.ApiKey    = $ApiKey

    # Quick connectivity check
    try {
        $health = Invoke-RestMethod -Uri (_ApiUrl '/api/health') -Headers (_Headers) -Method GET -ErrorAction Stop
        Write-Host "Connected to Optimizer server at $ServerUrl — status: $($health.status)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not reach server at $ServerUrl. Connection settings saved anyway."
        Write-Warning $_.Exception.Message
    }
}

<#
.SYNOPSIS
    Get the health and version information of the Optimizer cloud server.

.DESCRIPTION
    Calls GET /api/health and returns the server status object.

.EXAMPLE
    Get-OptimizerStatus

.OUTPUTS
    PSCustomObject with properties: status, version, time
#>
function Get-OptimizerStatus {
    [CmdletBinding()]
    param()

    return _Get '/api/health'
}

<#
.SYNOPSIS
    Browse the Optimizer marketplace for profiles.

.DESCRIPTION
    Calls GET /api/marketplace with optional filtering. Returns an array
    of marketplace listing objects.

.PARAMETER Category
    Filter results by category name.

.PARAMETER Search
    Free-text search term.

.PARAMETER Page
    Page number (1-based). Default: 1.

.EXAMPLE
    Get-OptimizerProfile

.EXAMPLE
    Get-OptimizerProfile -Category 'Gaming' -Search 'fps'

.OUTPUTS
    Array of marketplace listing objects (name, author, downloads, rating, etc.)
#>
function Get-OptimizerProfile {
    [CmdletBinding()]
    param(
        [string]$Category,
        [string]$Search,
        [int]$Page = 1
    )

    $qs = @()
    if ($Category) { $qs += "category=$([Uri]::EscapeDataString($Category))" }
    if ($Search)   { $qs += "search=$([Uri]::EscapeDataString($Search))" }
    if ($Page -gt 1) { $qs += "page=$Page" }
    $path = '/api/marketplace' + (($qs.Count -gt 0) ? ('?' + ($qs -join '&')) : '')

    $resp = _Get $path
    if ($null -eq $resp) { return }

    return $resp.listings
}

<#
.SYNOPSIS
    Browse the Optimizer plugin marketplace.

.DESCRIPTION
    Calls GET /api/plugins with optional filtering. Returns an array of
    plugin listing objects.

.PARAMETER Search
    Free-text search term.

.PARAMETER Category
    Filter results by category name.

.PARAMETER Page
    Page number (1-based). Default: 1.

.EXAMPLE
    Get-OptimizerPlugin

.EXAMPLE
    Get-OptimizerPlugin -Search 'privacy' | Select-Object pluginId, name, downloads

.OUTPUTS
    Array of plugin listing objects (pluginId, name, author, downloads, rating, etc.)
#>
function Get-OptimizerPlugin {
    [CmdletBinding()]
    param(
        [string]$Search,
        [string]$Category,
        [int]$Page = 1
    )

    $qs = @()
    if ($Search)   { $qs += "search=$([Uri]::EscapeDataString($Search))" }
    if ($Category) { $qs += "category=$([Uri]::EscapeDataString($Category))" }
    if ($Page -gt 1) { $qs += "page=$Page" }
    $path = '/api/plugins' + (($qs.Count -gt 0) ? ('?' + ($qs -join '&')) : '')

    $resp = _Get $path
    if ($null -eq $resp) { return }

    return $resp.listings
}

<#
.SYNOPSIS
    Pull sync items from the cloud server.

.DESCRIPTION
    Calls GET /api/sync?since=<cursor> and returns the list of sync items.
    Requires a key with the sync:read scope.

.PARAMETER Since
    Version cursor — only items newer than this are returned. Default: 0 (all items).

.EXAMPLE
    Get-OptimizerSyncItem

.EXAMPLE
    Get-OptimizerSyncItem -Since 1000 | Where-Object itemType -eq 'Profile'

.OUTPUTS
    Array of sync item objects (itemType, itemId, version, updatedAtUtc, payload, isDeleted)
#>
function Get-OptimizerSyncItem {
    [CmdletBinding()]
    param(
        [long]$Since = 0
    )

    $resp = _Get "/api/sync?since=$Since"
    if ($null -eq $resp) { return }

    return $resp.items
}

<#
.SYNOPSIS
    Register a webhook subscription on the Optimizer cloud server.

.DESCRIPTION
    Calls POST /api/webhooks. Returns the created webhook including the
    signing secret — store this securely; it is never shown again.

.PARAMETER Url
    The URL that Optimizer will POST events to.

.PARAMETER EventTypes
    Optional array of event type patterns to subscribe to.
    Use '*' to subscribe to all events. Default: all events.

.EXAMPLE
    $hook = Register-OptimizerWebhook -Url 'https://example.com/hooks/optimizer'
    Write-Host "Secret: $($hook.secret)"

.EXAMPLE
    Register-OptimizerWebhook -Url 'https://zapier.com/hooks/...' -EventTypes @('sync.*', 'alert.*')

.OUTPUTS
    CreatedWebhook object: id, url, secret, eventTypes
#>
function Register-OptimizerWebhook {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Url,

        [string[]]$EventTypes = @('*')
    )

    $body = @{
        url        = $Url
        eventTypes = $EventTypes
    }

    return _Post '/api/webhooks' $body
}

<#
.SYNOPSIS
    List webhook subscriptions registered on the Optimizer cloud server.

.DESCRIPTION
    Calls GET /api/webhooks and returns all webhooks for the current API key owner.

.EXAMPLE
    Get-OptimizerWebhook

.EXAMPLE
    Get-OptimizerWebhook | Where-Object isActive -eq $true

.OUTPUTS
    Array of WebhookDto objects: id, url, eventTypes, isActive, createdAtUtc, lastDeliveryAtUtc, consecutiveFailures
#>
function Get-OptimizerWebhook {
    [CmdletBinding()]
    param()

    return _Get '/api/webhooks'
}

<#
.SYNOPSIS
    Delete (unregister) a webhook subscription.

.DESCRIPTION
    Calls DELETE /api/webhooks/{id}.

.PARAMETER Id
    The GUID of the webhook to delete (from Get-OptimizerWebhook or Register-OptimizerWebhook).

.EXAMPLE
    Unregister-OptimizerWebhook -Id '3f2504e0-4f89-11d3-9a0c-0305e82c3301'

.EXAMPLE
    Get-OptimizerWebhook | Where-Object consecutiveFailures -gt 5 | ForEach-Object {
        Unregister-OptimizerWebhook -Id $_.id
    }
#>
function Unregister-OptimizerWebhook {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [string]$Id
    )

    if ($PSCmdlet.ShouldProcess($Id, "Delete webhook")) {
        $ok = _Delete "/api/webhooks/$Id"
        if ($ok) {
            Write-Verbose "Webhook $Id deleted."
        }
    }
}
