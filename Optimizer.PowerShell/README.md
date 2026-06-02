# Optimizer PowerShell Module

PowerShell 7+ module for the **Optimizer cloud server** REST API.  
Wraps the full API surface with idiomatic cmdlet-style functions using `Invoke-RestMethod`.

## Requirements

- PowerShell 7.0 or later (`pwsh`)
- An Optimizer.Server instance running and reachable
- An API key (see [Getting an API Key](#getting-an-api-key))

## Installation

```powershell
# Import directly from the repository
Import-Module 'L:\Projects\Optimizer.PowerShell\Optimizer.psd1' -Force

# Or copy the folder to a PSModulePath directory and import by name
Import-Module Optimizer
```

## Getting an API Key

API keys are created interactively via a JWT session (magic-link login):

1. Open the Optimizer desktop app
2. Go to **Settings → Developer**
3. Click **Create API Key**, choose a name and scopes
4. Copy the key — it is only shown once

Alternatively, call `POST /api/keys` with a JWT Bearer token obtained via magic-link auth.

## Quick Start

```powershell
# 1. Store your key (or set env vars beforehand)
$env:OPTIMIZER_API_KEY = 'opk_your_key_here'

# 2. Connect
Connect-Optimizer -ServerUrl 'http://localhost:5000' -ApiKey $env:OPTIMIZER_API_KEY

# 3. Check status
Get-OptimizerStatus

# 4. Browse plugins
Get-OptimizerPlugin -Search 'privacy'
```

## Functions

### `Connect-Optimizer`

Stores the server URL and API key in module scope for use by all other cmdlets.

```powershell
Connect-Optimizer -ServerUrl 'http://localhost:5000' -ApiKey 'opk_abc123'
```

Parameters:
- `-ServerUrl` (required) — base URL of the Optimizer.Server instance
- `-ApiKey` (required) — API key with appropriate scopes

---

### `Get-OptimizerStatus`

Returns the server health and version information.

```powershell
$status = Get-OptimizerStatus
Write-Host "Server: $($status.status)  Version: $($status.version)"
```

---

### `Get-OptimizerProfile`

Browse the marketplace profile listings.

```powershell
# All profiles
Get-OptimizerProfile

# Filter by category and search term
Get-OptimizerProfile -Category 'Gaming' -Search 'fps'

# Get top downloads
Get-OptimizerProfile | Sort-Object downloads -Descending | Select-Object -First 5 name, downloads, averageRating
```

Parameters:
- `-Category` — filter by category
- `-Search` — free-text search
- `-Page` — page number (default: 1)

---

### `Get-OptimizerPlugin`

Browse the plugin marketplace.

```powershell
# All plugins
Get-OptimizerPlugin

# Search for privacy-related plugins
Get-OptimizerPlugin -Search 'privacy' | Select-Object pluginId, name, downloads

# Only verified plugins
Get-OptimizerPlugin | Where-Object verified -eq $true
```

Parameters:
- `-Search` — free-text search
- `-Category` — filter by category
- `-Page` — page number (default: 1)

---

### `Get-OptimizerSyncItem`

Pull sync items from the cloud server. Requires an API key with the `sync:read` scope.

```powershell
# All items
Get-OptimizerSyncItem

# Only items newer than cursor 1000
Get-OptimizerSyncItem -Since 1000

# Count items by type
Get-OptimizerSyncItem | Group-Object itemType | Select-Object Name, Count

# Find deleted items
Get-OptimizerSyncItem | Where-Object isDeleted -eq $true
```

Parameters:
- `-Since` — version cursor (default: 0 = all items)

---

### `Register-OptimizerWebhook`

Register a webhook endpoint to receive Optimizer events.

```powershell
# Subscribe to all events
$hook = Register-OptimizerWebhook -Url 'https://example.com/optimizer-hook'
Write-Host "Webhook secret: $($hook.secret)"   # Store this! Never shown again.

# Subscribe to specific event types only
Register-OptimizerWebhook -Url 'https://zapier.com/hooks/...' -EventTypes @('sync.*', 'alert.*')
```

Parameters:
- `-Url` (required) — URL that will receive POST requests
- `-EventTypes` — array of event type patterns (default: `@('*')` = all events)

Returns: `CreatedWebhook` with `id`, `url`, `secret`, `eventTypes`

---

### `Get-OptimizerWebhook`

List all registered webhooks.

```powershell
Get-OptimizerWebhook

# Find unhealthy webhooks
Get-OptimizerWebhook | Where-Object consecutiveFailures -gt 3
```

---

### `Unregister-OptimizerWebhook`

Delete a webhook subscription.

```powershell
# Delete by ID
Unregister-OptimizerWebhook -Id '3f2504e0-4f89-11d3-9a0c-0305e82c3301'

# Delete all failing webhooks
Get-OptimizerWebhook |
    Where-Object consecutiveFailures -gt 5 |
    ForEach-Object { Unregister-OptimizerWebhook -Id $_.id }
```

Parameters:
- `-Id` (required) — GUID of the webhook to delete
- Supports `-WhatIf` and `-Confirm` via `SupportsShouldProcess`

---

## Environment Variables

You can pre-set these environment variables before importing the module:

| Variable              | Description                                   | Default                  |
|-----------------------|-----------------------------------------------|--------------------------|
| `OPTIMIZER_CLOUD_URL` | Base URL of the Optimizer.Server instance     | —                        |
| `OPTIMIZER_API_KEY`   | API key for authentication                    | —                        |

```powershell
$env:OPTIMIZER_CLOUD_URL = 'https://optimizer.example.com'
$env:OPTIMIZER_API_KEY   = 'opk_your_key_here'
Import-Module Optimizer
Connect-Optimizer -ServerUrl $env:OPTIMIZER_CLOUD_URL -ApiKey $env:OPTIMIZER_API_KEY
```

## Running the Tests

```powershell
# Smoke tests (no server required)
pwsh -File 'L:\Projects\Optimizer.PowerShell\Tests\Optimizer.Tests.ps1'

# With Pester 5+ (if installed)
Invoke-Pester 'L:\Projects\Optimizer.PowerShell\Tests\Optimizer.Tests.ps1' -Output Detailed
```
