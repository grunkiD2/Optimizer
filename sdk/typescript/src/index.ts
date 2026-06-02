// @optimizer/sdk — TypeScript SDK for the Optimizer cloud server API
// Generated against OpenAPI v1 (Optimizer.Server 1.0)
// Mirrors the server DTOs in Optimizer.Server/Models/*.cs

// ─────────────────────────────────────────────────────────────────────────────
// DTOs — mirrors Optimizer.Server/Models/*Dtos.cs
// ─────────────────────────────────────────────────────────────────────────────

export interface HealthResponse {
  status: string;
  version: string;
  time: string;
}

// Marketplace

export interface MarketplaceListingDto {
  id: string;
  publicId: string;
  name: string;
  authorDisplayName: string;
  description: string;
  category: string;
  tags: string[];
  optimizations: string[];
  downloads: number;
  averageRating: number;
  ratingCount: number;
  verified: boolean;
  featured: boolean;
}

export interface MarketplaceBrowseResponse {
  total: number;
  page: number;
  pageSize: number;
  listings: MarketplaceListingDto[];
}

// Plugins

export interface PluginListingDto {
  pluginId: string;
  name: string;
  authorDisplayName: string;
  description: string;
  category: string;
  downloads: number;
  averageRating: number;
  ratingCount: number;
  verified: boolean;
}

export interface PluginBrowseResponse {
  total: number;
  page: number;
  pageSize: number;
  listings: PluginListingDto[];
}

export interface PluginDetailDto extends PluginListingDto {
  manifestYaml: string;
  signature: string | null;
}

// Sync

export interface SyncItemDto {
  itemType: string;
  itemId: string;
  version: number;
  updatedAtUtc: string;
  payload: string;
  isDeleted: boolean;
}

export interface SyncPullResponse {
  cursor: number;
  serverVersion: number;
  items: SyncItemDto[];
}

export interface SyncPushItem {
  itemType: string;
  itemId: string;
  payload: string;
  isDeleted?: boolean;
}

export interface SyncPushResponse {
  serverVersion: number;
  results: Array<{ itemType: string; itemId: string; version: number }>;
}

// Webhooks

export interface CreateWebhookRequest {
  url: string;
  eventTypes?: string[];
}

export interface WebhookDto {
  id: string;
  url: string;
  eventTypes: string[];
  isActive: boolean;
  createdAtUtc: string;
  lastDeliveryAtUtc: string | null;
  consecutiveFailures: number;
}

export interface CreatedWebhookDto {
  id: string;
  url: string;
  /** Signing secret — returned only on creation. Store it securely; never shown again. */
  secret: string;
  eventTypes: string[];
}

// Browse options (shared)

export interface BrowseOptions {
  category?: string;
  search?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}

// API error

export interface ApiError {
  code: string;
  message: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// OptimizerClient
// ─────────────────────────────────────────────────────────────────────────────

export class OptimizerClient {
  /**
   * @param baseUrl  Base URL of the Optimizer.Server instance, e.g. "http://localhost:5000"
   * @param apiKey   API key created via Settings → Developer (or POST /api/keys with JWT)
   */
  constructor(
    private readonly baseUrl: string,
    private readonly apiKey: string,
  ) {
    // Normalise: remove trailing slash
    this.baseUrl = baseUrl.replace(/\/$/, "");
  }

  // ── Internal helpers ────────────────────────────────────────────────────────

  private headers(): Record<string, string> {
    return {
      "X-Api-Key": this.apiKey,
      "Content-Type": "application/json",
    };
  }

  private url(path: string): string {
    return `${this.baseUrl}${path}`;
  }

  private buildQs(opts: BrowseOptions): string {
    const parts: string[] = [];
    if (opts.category) parts.push(`category=${encodeURIComponent(opts.category)}`);
    if (opts.search)   parts.push(`search=${encodeURIComponent(opts.search)}`);
    if (opts.sort)     parts.push(`sort=${encodeURIComponent(opts.sort)}`);
    if (opts.page && opts.page > 1) parts.push(`page=${opts.page}`);
    if (opts.pageSize) parts.push(`pageSize=${opts.pageSize}`);
    return parts.length > 0 ? `?${parts.join("&")}` : "";
  }

  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
  ): Promise<T> {
    const res = await fetch(this.url(path), {
      method,
      headers: this.headers(),
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });

    if (!res.ok) {
      let detail = "";
      try {
        const err = (await res.json()) as ApiError;
        detail = ` — ${err.code}: ${err.message}`;
      } catch {
        detail = ` — ${await res.text()}`;
      }
      throw new Error(`Optimizer API error ${res.status}${detail}`);
    }

    // 204 No Content
    if (res.status === 204) {
      return undefined as unknown as T;
    }

    return res.json() as Promise<T>;
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * GET /api/health
   * Returns the server health status and version.
   */
  async health(): Promise<HealthResponse> {
    return this.request<HealthResponse>("GET", "/api/health");
  }

  /**
   * GET /api/marketplace
   * Browse the profile marketplace with optional filtering.
   */
  async browseMarketplace(
    opts: BrowseOptions = {},
  ): Promise<MarketplaceBrowseResponse> {
    return this.request<MarketplaceBrowseResponse>(
      "GET",
      `/api/marketplace${this.buildQs(opts)}`,
    );
  }

  /**
   * GET /api/marketplace/{publicId}
   * Get a single marketplace listing by its public ID.
   */
  async getMarketplaceListing(publicId: string): Promise<MarketplaceListingDto> {
    return this.request<MarketplaceListingDto>(
      "GET",
      `/api/marketplace/${encodeURIComponent(publicId)}`,
    );
  }

  /**
   * GET /api/plugins
   * Browse the plugin marketplace with optional filtering.
   */
  async browsePlugins(opts: BrowseOptions = {}): Promise<PluginBrowseResponse> {
    return this.request<PluginBrowseResponse>(
      "GET",
      `/api/plugins${this.buildQs(opts)}`,
    );
  }

  /**
   * GET /api/plugins/{pluginId}
   * Get full plugin detail including manifest YAML and signature.
   */
  async getPlugin(pluginId: string): Promise<PluginDetailDto> {
    return this.request<PluginDetailDto>(
      "GET",
      `/api/plugins/${encodeURIComponent(pluginId)}`,
    );
  }

  /**
   * GET /api/sync?since={cursor}
   * Pull sync items newer than the given cursor.
   * Requires an API key with the sync:read scope.
   */
  async getSyncItems(since = 0): Promise<SyncPullResponse> {
    return this.request<SyncPullResponse>("GET", `/api/sync?since=${since}`);
  }

  /**
   * POST /api/sync
   * Push local changes to the server.
   * Requires an API key with the sync:write scope.
   */
  async pushSyncItems(items: SyncPushItem[]): Promise<SyncPushResponse> {
    return this.request<SyncPushResponse>("POST", "/api/sync", { items });
  }

  /**
   * POST /api/webhooks
   * Register a new webhook subscription.
   * Returns a CreatedWebhookDto including the signing secret (shown once only).
   */
  async registerWebhook(
    url: string,
    eventTypes?: string[],
  ): Promise<CreatedWebhookDto> {
    return this.request<CreatedWebhookDto>("POST", "/api/webhooks", {
      url,
      eventTypes,
    } satisfies CreateWebhookRequest);
  }

  /**
   * GET /api/webhooks
   * List all webhooks registered for the current API key owner.
   */
  async listWebhooks(): Promise<WebhookDto[]> {
    return this.request<WebhookDto[]>("GET", "/api/webhooks");
  }

  /**
   * DELETE /api/webhooks/{id}
   * Delete a webhook subscription by its ID.
   */
  async deleteWebhook(id: string): Promise<void> {
    return this.request<void>("DELETE", `/api/webhooks/${encodeURIComponent(id)}`);
  }
}
