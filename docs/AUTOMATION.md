# Optimizer Automation Guide

This guide shows how to automate Optimizer using its API key-authenticated REST API,
CLI cloud commands, PowerShell module, and TypeScript SDK — and how to integrate
with automation platforms (IFTTT, Zapier, Power Automate) via webhooks.

---

## Table of Contents

1. [Getting an API Key](#1-getting-an-api-key)
2. [CLI Cloud Commands](#2-cli-cloud-commands)
3. [PowerShell Examples](#3-powershell-examples)
4. [TypeScript SDK Examples](#4-typescript-sdk-examples)
5. [Webhook Recipes](#5-webhook-recipes)
   - [Webhook Payload Shape](#webhook-payload-shape)
   - [Signature Verification](#signature-verification-x-optimizer-signature)
   - [IFTTT Recipe](#ifttt-recipe)
   - [Zapier Recipe](#zapier-recipe)
   - [Power Automate Recipe](#power-automate-recipe)

---

## 1. Getting an API Key

API keys authenticate CLI, SDK, and webhook calls. They are created interactively
via a JWT session (magic-link login) because a key cannot mint other keys.

### Via the Desktop App

1. Open the **Optimizer** desktop app
2. Navigate to **Settings → Developer**
3. Click **Create API Key**
4. Enter a name (e.g., `automation-script`) and select the scopes your use case requires
5. Click **Create** — copy the key immediately, it is shown only once

### Via the API (requires JWT)

```bash
# After obtaining a JWT Bearer token via magic-link login:
curl -X POST http://localhost:5000/api/keys \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "my-automation-key",
    "scopes": ["sync:read", "sync:write", "webhooks:manage"]
  }'
```

### Scopes Reference

| Scope              | Description                                   |
|--------------------|-----------------------------------------------|
| `sync:read`        | Pull sync items from the server               |
| `sync:write`       | Push sync items to the server                 |
| `plugins:manage`   | Submit and manage plugins                     |
| `webhooks:manage`  | Register, list, and delete webhooks           |
| `*`                | All scopes (admin key — use carefully)        |

---

## 2. CLI Cloud Commands

Set environment variables first:

```powershell
$env:OPTIMIZER_CLOUD_URL = 'http://localhost:5000'
$env:OPTIMIZER_API_KEY   = 'opk_your_key_here'
```

Then use the `optimizer cloud` command group:

```powershell
# Check server health and connection
optimizer cloud status

# Pull sync items and report counts (--since 0 = all items)
optimizer cloud sync
optimizer cloud sync --since 1000

# Browse the marketplace
optimizer cloud marketplace
optimizer cloud marketplace --category Gaming
optimizer cloud marketplace --search fps --page 2

# Browse plugins
optimizer cloud plugins
optimizer cloud plugins --search privacy
optimizer cloud plugins --category Security

# API key management note
optimizer cloud keys list
# → prints: "API key management requires an interactive JWT session."
```

### Scripting with the CLI

```powershell
# Scheduled task: check server status every 5 minutes
while ($true) {
    optimizer cloud status
    Start-Sleep -Seconds 300
}

# Check sync health in a CI pipeline
$result = optimizer cloud sync 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Sync check failed"
    exit 1
}
```

---

## 3. PowerShell Examples

```powershell
Import-Module 'L:\Projects\Optimizer.PowerShell\Optimizer.psd1'
Connect-Optimizer -ServerUrl 'http://localhost:5000' -ApiKey $env:OPTIMIZER_API_KEY
```

### Health check

```powershell
$status = Get-OptimizerStatus
if ($status.status -ne 'ok') {
    Send-MailMessage -To 'admin@example.com' -Subject "Optimizer server degraded!"
}
```

### Browse and filter plugins

```powershell
# All verified plugins sorted by downloads
Get-OptimizerPlugin |
    Where-Object verified -eq $true |
    Sort-Object downloads -Descending |
    Select-Object pluginId, name, downloads, averageRating |
    Format-Table
```

### Sync monitoring

```powershell
# Count items by type and check for unexpected deletions
$items = Get-OptimizerSyncItem
$deleted = $items | Where-Object isDeleted -eq $true
if ($deleted.Count -gt 0) {
    Write-Warning "$($deleted.Count) deleted sync items found"
    $deleted | Select-Object itemType, itemId, updatedAtUtc | Format-Table
}
```

### Webhook management

```powershell
# Register a webhook for all events
$hook = Register-OptimizerWebhook -Url 'https://hooks.example.com/optimizer'
# IMPORTANT: Save the secret — it is never shown again
$secret = $hook.secret
Set-Content -Path 'webhook-secret.txt' -Value $secret

# Clean up failing webhooks
Get-OptimizerWebhook |
    Where-Object consecutiveFailures -gt 5 |
    ForEach-Object {
        Write-Host "Removing failing webhook: $($_.url)"
        Unregister-OptimizerWebhook -Id $_.id
    }
```

---

## 4. TypeScript SDK Examples

```typescript
import { OptimizerClient } from '@optimizer/sdk';

const client = new OptimizerClient(
  process.env.OPTIMIZER_CLOUD_URL ?? 'http://localhost:5000',
  process.env.OPTIMIZER_API_KEY!,
);
```

### Basic usage

```typescript
// Health check
const health = await client.health();
console.log(`Server ${health.status} v${health.version}`);

// Browse plugins
const plugins = await client.browsePlugins({ search: 'privacy' });
console.log(`Found ${plugins.total} plugins`);
plugins.listings.forEach(p => console.log(`  ${p.name} (${p.downloads} downloads)`));
```

### Sync automation

```typescript
// Pull all sync items and group by type
const pull = await client.getSyncItems();
const byType = pull.items.reduce((acc, item) => {
  acc[item.itemType] = (acc[item.itemType] ?? 0) + 1;
  return acc;
}, {} as Record<string, number>);
console.table(byType);
```

### Register a webhook from code

```typescript
const hook = await client.registerWebhook(
  'https://my-app.example.com/optimizer-events',
  ['sync.*', 'alert.*'],
);
// Store hook.secret securely — used to verify incoming signatures
await saveToSecretsManager('optimizer-webhook-secret', hook.secret);
```

---

## 5. Webhook Recipes

Webhooks let external services react to Optimizer events in real time.
Register a webhook pointing at your automation platform's ingest URL,
then the platform routes events to your workflows.

### Webhook Payload Shape

Every HTTP POST sent by Optimizer to your endpoint has this JSON body:

```json
{
  "type":         "alert.cpu_temperature",
  "title":        "CPU Temperature Alert",
  "detail":       "CPU temperature exceeded 90°C threshold",
  "timestampUtc": "2026-06-02T14:30:00Z",
  "data": {
    "temperature": "92",
    "threshold":   "90",
    "sensor":      "CPU Package"
  }
}
```

Headers on every delivery:

| Header                    | Value                                              |
|---------------------------|----------------------------------------------------|
| `Content-Type`            | `application/json`                                 |
| `X-Optimizer-Event-Type`  | Event type string (e.g. `alert.cpu_temperature`)   |
| `X-Optimizer-Signature`   | HMAC-SHA256 hex digest (see below)                 |
| `X-Optimizer-Delivery-Id` | Unique delivery UUID                               |

---

### Signature Verification (`X-Optimizer-Signature`)

Verify the `X-Optimizer-Signature` header to confirm the request is genuinely
from Optimizer and hasn't been tampered with.

The signature is `HMAC-SHA256(webhookSecret, requestBodyBytes)` encoded as lowercase hex.

#### Node.js / TypeScript

```typescript
import { createHmac, timingSafeEqual } from 'crypto';

function verifyOptimizerSignature(
  secret: string,
  rawBody: Buffer,
  signatureHeader: string,
): boolean {
  const expected = createHmac('sha256', secret)
    .update(rawBody)
    .digest('hex');

  try {
    return timingSafeEqual(
      Buffer.from(expected, 'hex'),
      Buffer.from(signatureHeader, 'hex'),
    );
  } catch {
    return false;
  }
}

// Express.js middleware example
app.post('/optimizer-events', express.raw({ type: 'application/json' }), (req, res) => {
  const sig = req.headers['x-optimizer-signature'] as string;
  if (!verifyOptimizerSignature(WEBHOOK_SECRET, req.body, sig)) {
    return res.status(401).json({ error: 'Invalid signature' });
  }
  const event = JSON.parse(req.body.toString());
  // handle event...
  res.sendStatus(200);
});
```

#### PowerShell

```powershell
function Test-OptimizerSignature {
    param([string]$Secret, [string]$RawBody, [string]$Signature)

    $key  = [System.Text.Encoding]::UTF8.GetBytes($Secret)
    $data = [System.Text.Encoding]::UTF8.GetBytes($RawBody)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($key)
    $hash = ($hmac.ComputeHash($data) | ForEach-Object { '{0:x2}' -f $_ }) -join ''
    return $hash -eq $Signature
}
```

#### C# / ASP.NET

```csharp
using System.Security.Cryptography;
using System.Text;

bool VerifySignature(string secret, byte[] body, string signatureHeader)
{
    var key  = Encoding.UTF8.GetBytes(secret);
    var hash = HMACSHA256.HashData(key, body);
    var expected = Convert.ToHexString(hash).ToLowerInvariant();
    return CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(expected),
        Encoding.ASCII.GetBytes(signatureHeader));
}
```

---

### IFTTT Recipe

**Scenario:** When CPU temperature exceeds a threshold → send a phone notification.

IFTTT uses its **Webhooks** service as the bridge between Optimizer and IFTTT applets.

#### Steps

1. Go to [ifttt.com/maker_webhooks](https://ifttt.com/maker_webhooks) and get your
   Maker URL: `https://maker.ifttt.com/trigger/{event}/json/with/key/{your-key}`

2. Create an IFTTT Applet:
   - **If** → Webhooks — "Receive a web request with a JSON payload"
   - Event name: `optimizer_alert`
   - **Then** → Notifications — "Send a rich notification from the IFTTT app"
   - Message: `{{JsonPayload.title}}: {{JsonPayload.detail}}`

3. Register the Optimizer webhook pointing at your IFTTT Maker URL:

   ```powershell
   Connect-Optimizer -ServerUrl $env:OPTIMIZER_CLOUD_URL -ApiKey $env:OPTIMIZER_API_KEY
   Register-OptimizerWebhook `
       -Url  'https://maker.ifttt.com/trigger/optimizer_alert/json/with/key/YOUR_IFTTT_KEY' `
       -EventTypes @('alert.*')
   ```

4. Trigger a test from the Optimizer desktop app or via the CLI:

   ```bash
   curl -X POST http://localhost:5000/api/events \
     -H "X-Api-Key: $OPTIMIZER_API_KEY" \
     -H "Content-Type: application/json" \
     -d '{"type":"alert.cpu_temperature","title":"CPU Temp Alert","detail":"CPU at 92°C","timestampUtc":"2026-06-02T14:00:00Z","data":{"temperature":"92"}}'
   ```

5. Your phone receives the IFTTT notification within seconds.

> Note: IFTTT's Webhooks service doesn't validate `X-Optimizer-Signature`.
> Consider using a thin relay function (e.g., a Cloudflare Worker) to verify
> the signature before forwarding to IFTTT for production use.

---

### Zapier Recipe

**Scenario:** When any sync event occurs → post a Slack message.

#### Steps

1. In Zapier, create a new Zap:
   - **Trigger**: Webhooks by Zapier → "Catch Hook"
   - Copy the generated webhook URL (e.g., `https://hooks.zapier.com/hooks/catch/12345/abcdef/`)

2. **Action**: Slack → "Send Channel Message"
   - Channel: `#optimizer-alerts`
   - Message: `:gear: Optimizer sync event: {{title}} — {{detail}} at {{timestampUtc}}`

3. Register the webhook in Optimizer:

   ```typescript
   import { OptimizerClient } from '@optimizer/sdk';
   const client = new OptimizerClient(process.env.OPTIMIZER_CLOUD_URL!, process.env.OPTIMIZER_API_KEY!);
   const hook = await client.registerWebhook(
     'https://hooks.zapier.com/hooks/catch/12345/abcdef/',
     ['sync.*'],
   );
   console.log('Webhook registered:', hook.id);
   ```

4. Turn on the Zap. Zapier will now post a Slack message whenever a sync event fires.

---

### Power Automate Recipe

**Scenario:** When a `sync.completed` event fires → send a Microsoft Teams message.

#### Steps

1. In Power Automate, create a new **Automated cloud flow**:
   - **Trigger**: "When an HTTP request is received" (from the HTTP connector)
   - Copy the generated HTTP POST URL
   - In the request body JSON schema, paste:
     ```json
     {
       "type": "object",
       "properties": {
         "type":         { "type": "string" },
         "title":        { "type": "string" },
         "detail":       { "type": "string" },
         "timestampUtc": { "type": "string" },
         "data":         { "type": "object" }
       }
     }
     ```

2. **Action**: Microsoft Teams → "Post a message in a chat or channel"
   - Team: Your team
   - Channel: `General`
   - Message:
     ```
     🔄 Optimizer Sync: @{triggerBody()?['title']}
     @{triggerBody()?['detail']}
     Time: @{triggerBody()?['timestampUtc']}
     ```

3. **Optional**: Add a "Condition" step before the Teams action to verify the signature:
   - Check that `X-Optimizer-Signature` matches the HMAC-SHA256 of the body
   - Power Automate can call an Azure Function or Cloudflare Worker to do this verification

4. Register the webhook in Optimizer:

   ```powershell
   Connect-Optimizer -ServerUrl $env:OPTIMIZER_CLOUD_URL -ApiKey $env:OPTIMIZER_API_KEY
   $hook = Register-OptimizerWebhook `
       -Url        'https://prod-12.eastus.logic.azure.com:443/workflows/...' `
       -EventTypes @('sync.completed', 'sync.failed')
   Write-Host "Hook ID: $($hook.id)"
   Write-Host "Secret: $($hook.secret)"  # Store in Azure Key Vault
   ```

5. Save the flow. Teams notifications will appear whenever Optimizer fires a `sync.*` event.

---

## Summary

| Tool            | Auth                | Primary use case                             |
|-----------------|---------------------|----------------------------------------------|
| `optimizer cloud` CLI | `OPTIMIZER_API_KEY` env var | Shell scripts, CI pipelines, cron jobs |
| PowerShell module   | `Connect-Optimizer` | IT automation, scheduled tasks, admin scripts |
| TypeScript SDK      | Constructor param   | Node.js apps, browser extensions, Next.js   |
| Direct REST API     | `X-Api-Key` header  | Any HTTP client, curl, custom integrations  |
| Webhooks            | `X-Optimizer-Signature` | IFTTT, Zapier, Power Automate, custom servers |
