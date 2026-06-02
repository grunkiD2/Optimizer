# @optimizer/sdk

TypeScript SDK for the **Optimizer cloud server** API.  
Hand-written against OpenAPI v1 (Optimizer.Server 1.0). Works in Node.js 18+ and modern browsers via the native `fetch` API.

## Installation

```bash
# When published to npm:
npm install @optimizer/sdk

# From the local repository:
import { OptimizerClient } from './path/to/sdk/typescript/src/index.ts';
```

## Getting an API Key

1. Open the Optimizer desktop app
2. Go to **Settings → Developer**
3. Click **Create API Key**, choose a name and scopes
4. Copy the key — it is shown only once

## Quick Start

```typescript
import { OptimizerClient } from '@optimizer/sdk';

const client = new OptimizerClient(
  'http://localhost:5000',
  process.env.OPTIMIZER_API_KEY!,
);

// Check server health
const status = await client.health();
console.log(status.status, status.version);

// Browse plugins
const plugins = await client.browsePlugins({ search: 'privacy' });
plugins.listings.forEach(p => console.log(p.name, p.downloads));
```

## API Reference

### `new OptimizerClient(baseUrl, apiKey)`

```typescript
const client = new OptimizerClient('http://localhost:5000', 'opk_your_key');
```

---

### `client.health()`

Returns `HealthResponse` — server status and version.

```typescript
const { status, version, time } = await client.health();
```

---

### `client.browseMarketplace(opts?)`

Browse the profile marketplace.

```typescript
// All profiles
const all = await client.browseMarketplace();

// Filter
const gaming = await client.browseMarketplace({
  category: 'Gaming',
  search: 'fps',
  page: 1,
  pageSize: 20,
});
console.log(`${gaming.total} results`);
gaming.listings.forEach(l => console.log(l.name, '★', l.averageRating));
```

---

### `client.getMarketplaceListing(publicId)`

```typescript
const listing = await client.getMarketplaceListing('my-gaming-profile');
```

---

### `client.browsePlugins(opts?)`

Browse the plugin marketplace.

```typescript
const privacyPlugins = await client.browsePlugins({ search: 'privacy' });
const verifiedOnly = privacyPlugins.listings.filter(p => p.verified);
```

---

### `client.getPlugin(pluginId)`

Returns full plugin detail including manifest YAML.

```typescript
const detail = await client.getPlugin('optimizer.my-plugin');
console.log(detail.manifestYaml);
```

---

### `client.getSyncItems(since?)`

Pull sync items from the server. Requires an API key with the `sync:read` scope.

```typescript
// All items
const { items, cursor, serverVersion } = await client.getSyncItems();

// Only items newer than a saved cursor
const newItems = await client.getSyncItems(savedCursor);
```

---

### `client.pushSyncItems(items)`

Push local changes to the server. Requires `sync:write` scope.

```typescript
const result = await client.pushSyncItems([
  {
    itemType: 'Profile',
    itemId: 'my-profile-id',
    payload: JSON.stringify(profileData),
  },
]);
console.log('Server version:', result.serverVersion);
```

---

### `client.registerWebhook(url, eventTypes?)`

Register a webhook. The returned `secret` is shown **once only** — store it securely.

```typescript
const hook = await client.registerWebhook(
  'https://my-server.example.com/optimizer-events',
  ['sync.*', 'alert.*'],  // omit for all events
);
console.log('Webhook ID:', hook.id);
console.log('Secret:', hook.secret);  // Store this!
```

---

### `client.listWebhooks()`

```typescript
const webhooks = await client.listWebhooks();
const unhealthy = webhooks.filter(w => w.consecutiveFailures > 3);
```

---

### `client.deleteWebhook(id)`

```typescript
await client.deleteWebhook('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
```

---

## Error Handling

All methods throw an `Error` if the HTTP response is not successful.

```typescript
try {
  const plugins = await client.browsePlugins();
} catch (err) {
  // err.message: "Optimizer API error 401 — unauthorized: ..."
  console.error('API call failed:', err);
}
```

## TypeScript Interfaces

All server DTOs are exported as TypeScript interfaces:

```typescript
import type {
  HealthResponse,
  MarketplaceListingDto,
  MarketplaceBrowseResponse,
  PluginListingDto,
  PluginBrowseResponse,
  PluginDetailDto,
  SyncItemDto,
  SyncPullResponse,
  SyncPushItem,
  SyncPushResponse,
  WebhookDto,
  CreatedWebhookDto,
  BrowseOptions,
  ApiError,
} from '@optimizer/sdk';
```

## Authentication

All requests use the `X-Api-Key` header. Create API keys in the desktop app under **Settings → Developer** or via `POST /api/keys` with a JWT Bearer token (requires magic-link login).

API key management (create/list/revoke keys) requires a JWT session and is not accessible via an API key itself, to prevent privilege escalation.
