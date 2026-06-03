# Claude-Powered Intent Assistant + Persistent Console — Design

> Spec captured 2026-06-03. Supersedes the local-LLM path of roadmap **Phase F (Theme 3: Voice +
> Conversational Assistant)** with a cloud (Claude API) intent path. The on-device Phi-3/ONNX
> runtime remains a documented future option for fully-offline users.

## Goal

Let the user talk to Optimizer in natural language. Claude interprets intent and maps it to the
app's existing capabilities via **tool-use (function calling)**. Read-only questions are answered
directly; anything that changes the system is **proposed, confirmed, then executed** through the
existing undo pipeline. The assistant lives in a **persistent docked console** alongside a live
**activity log**, so the user can chat and watch changes happen without leaving the current page.

## Foundational decisions (locked)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| API key custody | **Bring-your-own-key, client-side** | Simplest, most private (data goes to the user's own Anthropic account), zero server cost. Fits the privacy-by-default stance. |
| Key storage | **Windows DPAPI** (`ProtectedData`, CurrentUser) | Encrypted at rest in the existing `AppPaths` data dir. |
| Action boundary | **Propose → confirm → execute, with undo** | Read-only queries answered directly; system changes gated by a confirmation card and routed through existing `IUndoService`. |
| Tool architecture | **Command registry (auto-generated tools)** | One source of truth for invokable actions + safety metadata; consumed by chat, omnibox, and (later) the REST API. |
| Surface | **Persistent docked console** with **Activity + Assistant tabs**, **pop-out** to a floating window; **omnibox** (Ctrl+K) | Chat without leaving the page; terminal-style activity stream; detachable to a second monitor. |
| Default model | **`claude-sonnet-4-6`** (Haiku `claude-haiku-4-5-20251001` selectable) | Sonnet 4.6 = most reliable tool-use; Haiku for cheaper/faster routing. |
| Opt-in | **Off by default** | Consistent with the Privacy-First-AI theme; cloud is a deliberate, user-enabled trade. |

## Architecture

Five independently-testable layers plus the console host:

```
┌─ MainWindow (Grid) ──────────────────────────────────────────────┐
│ row 0: InfoBars                                                   │
│ row 1: NavigationView → ContentFrame   (pages swap here)          │
│ row 2: GridSplitter (drag to resize)                              │
│ row 3: ConsoleDock  ◄── persists across ALL pages                 │
│        ┌ Activity | Assistant ┐  [collapse] [pop-out]             │
└────────┴───────────────────────┴──────────────────────────────────┘
                                  │
            ┌─ Orchestration ─────▼──────────────────────────────┐
            │ IAssistantService — tool-use loop, confirmation     │
            │ gating, streams tokens to the Assistant tab         │
            └──────────┬───────────────────────────┬─────────────┘
                       │                           │
        ┌─ Claude client ─────────┐   ┌─ Command registry ───────────┐
        │ IClaudeClient (HttpClient│   │ ICommandRegistry — invokable  │
        │ → api.anthropic.com,     │   │ actions + safety metadata;    │
        │ SSE streaming, tool-use, │   │ generates Claude tool defs    │
        │ prompt caching)          │   └──────────────┬────────────────┘
        └───────────┬─────────────┘                  │
           ┌────────▼────────┐         ┌──────────────▼────────────────┐
           │ IApiKeyStore     │         │ existing services             │
           │ (DPAPI-encrypted)│         │ (Optimizer, Diagnostics, …)   │
           └─────────────────┘         └───────────────────────────────┘

        IEventBus  ──►  ConsoleViewModel (Activity tab)  ──►  live log lines
```

The console dock content is a single **`ConsolePanel` UserControl** (a `TabView` with *Activity*
and *Assistant* tabs). **Pop-out** hosts the *same* UserControl in a separate `ConsoleWindow`,
bound to the *same* singleton ViewModels — so chat history and the activity log survive
docking/undocking with no state loss. When popped out, row 3 collapses; closing the window re-docks.

## Components

### 1. Command registry (the core)

```csharp
public interface IAppCommand {
    string      Id { get; }                  // "apply_profile" → Claude tool name
    string      Description { get; }         // → Claude tool description
    JsonElement ParametersSchema { get; }    // JSON Schema → tool input_schema
    bool        IsReadOnly { get; }
    bool        RequiresConfirmation { get; }
    Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct);
}

public interface ICommandRegistry {
    void Register(IAppCommand cmd);
    IReadOnlyList<IAppCommand> Commands { get; }
    IAppCommand? Find(string id);
}

public record CommandResult(bool Success, string Summary, object? Data = null);
```

Initial command set (~11), each a thin wrapper over an existing service:

| Command | Type | Wraps |
|---------|------|-------|
| `get_metrics` | read | `ISystemMonitorService` |
| `get_top_processes` | read | `ISystemMonitorService` |
| `get_recommendations` | read | `IRecommendationsService` |
| `run_diagnostics_scan` | read | `IDiagnosticsService` |
| `get_bottlenecks` | read | `IBottleneckDetectorService` |
| `list_profiles` | read | `IWindowsOptimizerService` |
| `navigate_to_page` | read | `INavigationService` |
| `apply_profile` | **confirm** | `IWindowsOptimizerService.ApplyProfileAsync` |
| `apply_optimization` | **confirm** | `IWindowsOptimizerService.ApplyOptimizationAsync` |
| `run_cleanup` | **confirm** | `ApplyOptimizationAsync(ClearTemporaryFiles)` |
| `undo_last` | **confirm** | `IWindowsOptimizerService.UndoEntryAsync` |

Commands self-register at startup. Adding a capability later = one new class, no UI/client change.
Claude tools are generated from the registry: tool `name` = `Id`, `description` = `Description`,
`input_schema` = `ParametersSchema`. (The hand-mapped `ApiHostService` endpoints can migrate onto
this registry in a later cleanup — out of scope here, noted as follow-up.)

### 2. Claude client (`IClaudeClient`)

Thin `HttpClient` wrapper over the **Messages API** (`POST https://api.anthropic.com/v1/messages`,
`x-api-key` header, `anthropic-version: 2023-06-01`). **No third-party SDK dependency** — we control
the surface and mock the `HttpMessageHandler` in tests.

- **SSE streaming** for token-by-token UI output.
- **Tool-use**: tool definitions generated from the registry; handles `stop_reason: tool_use`.
- **Prompt caching**: system prompt + tool definitions marked with `cache_control` so repeated
  turns are cheaper/faster. (Exact cache-block placement per the `claude-api` skill at build time.)
- **Models**: default `claude-sonnet-4-6`; `claude-haiku-4-5-20251001` selectable in settings.
- **Errors** mapped to friendly results: 401 (invalid key), 429 (rate limit → backoff message),
  network failure. The API key is never written to logs (redacted in `EngineLog`).

### 3. Orchestration (`IAssistantService`)

Drives the conversation loop:

1. Build the system prompt: assistant role + a short live system-metrics summary (for grounding) +
   the safety rule that destructive actions require user confirmation.
2. POST messages + generated tools; **stream** assistant text to the Assistant tab.
3. If `stop_reason == tool_use`, for each `tool_use` block:
   - `IsReadOnly` / no-confirm → execute immediately, append `tool_result`.
   - `RequiresConfirmation` → emit a **ConfirmationRequest** (action chip) to the UI and **pause**.
     On **Approve** → execute → `tool_result`. On **Reject** → `tool_result` = "user declined".
4. Send `tool_result`s back; repeat until `stop_reason == end_turn`.
5. Command exceptions become `tool_result` error text so Claude can explain gracefully.

### 4. Secret storage (`IApiKeyStore`)

`SetKey(string) / string? GetKey() / bool HasKey / Clear()`, backed by **DPAPI**
(`System.Security.Cryptography.ProtectedData`, `CurrentUser` scope), stored as an encrypted blob in
the `AppPaths` data dir. Adds the `System.Security.Cryptography.ProtectedData` NuGet package.

### 5. Console — Activity tab (`ConsoleViewModel`)

- Subscribes to `IEventBus.Subscribe(...)`; renders each `OptimizerEvent` as a line:
  `HH:mm:ss  [glyph] Title — Detail`, colored by event type.
- **Seeds from `IEventBus.RecentEvents`** on first show so history is present immediately.
- Optionally pipes **`EngineLog`** writes in as a "verbose" stream (a lightweight event will be
  added to `EngineLog`); toggle to show/hide.
- `ObservableCollection<ConsoleLine>`, auto-scroll, Clear, Copy-all.

Because every optimization / profile / plugin / anomaly already publishes to the event bus, the
activity log lights up with no extra wiring at the call sites.

### 6. Assistant tab + shell integration

- **AssistantViewModel** hosted in the Assistant tab: message list (user / assistant / tool-result /
  confirmation), input box, streaming append, Approve/Reject chips, clear-conversation, model
  indicator, and a "set up your key" empty state linking to Settings.
- **Omnibox** (`Ctrl+K`) opens/focuses the Assistant tab seeded with the typed text.
- **`Ctrl+\`** ` toggles the console dock open/collapsed.
- The standalone full-page Assistant is **not** added to the nav (it would duplicate the dock).
- `MainWindow.xaml` gains rows 2–3 + the splitter; `MainWindow.xaml.cs` owns dock collapse/expand
  and the pop-out hand-off to `ConsoleWindow`.

### 7. Settings — "AI Assistant" section

Masked API-key box (Save/Clear) · model dropdown (Sonnet 4.6 / Haiku 4.5) · "Enable assistant"
toggle · "Allow assistant to perform actions" toggle · plain-language privacy note ("your messages
and a summary of system metrics are sent to Anthropic").

### 8. DI / configuration

Register `ICommandRegistry` (singleton, seeded at startup), the `IAppCommand` implementations,
`IClaudeClient`, `IAssistantService`, `IApiKeyStore`, `ConsoleViewModel`, and `AssistantViewModel`
in `App.xaml.cs`.

## Safety model

- Destructive commands carry `RequiresConfirmation = true`; the UI shows a confirmation card
  (command + human-readable args) before execution.
- **All mutations route through existing services → existing `IUndoService` capture applies
  automatically.** No new undo plumbing.
- Global **"Allow assistant to perform actions"** toggle. Off → confirm-commands are removed from
  the tool list entirely, degrading to a read-only / navigate-only assistant.
- API key never logged. Friendly in-chat errors for 401 / 429 / network failures.

## Testing (~26–30 new tests)

- **Registry**: register/find, duplicate id rejection, read-only vs confirm metadata.
- **Commands**: each `ExecuteAsync` with mocked services (routing + arg parsing).
- **Tool generation**: registry → tool defs (schema shape, names).
- **Claude client**: mocked `HttpMessageHandler` — parse a `tool_use` response, parse an SSE stream,
  map 401/429/network errors.
- **Orchestration**: `tool_use` → execute → `tool_result` → `end_turn`; confirm-pause-approve path;
  reject path; allow-actions=off forces read-only tool list.
- **ApiKeyStore**: set/get/clear round-trip (DPAPI), `HasKey`.
- **ConsoleViewModel**: event → line, seed from `RecentEvents`, verbose toggle, Clear.
- **ViewModels**: Assistant message/confirmation flow + no-key empty state.

## Build order (4 batches)

- **F1** — Command registry + initial commands + tests.
- **F2** — Claude client (HttpClient / SSE streaming / tool-use / prompt caching) + DPAPI key store
  + tests.
- **F3** — Assistant orchestration service (tool-use loop + confirmation gating) + tests.
- **F4** — Console dock (MainWindow restructure, `ConsolePanel`, Activity tab + `ConsoleViewModel`
  on the event bus, pop-out `ConsoleWindow`) + Assistant tab, omnibox, hotkeys, Settings section,
  DI wiring + ViewModel tests.

Each batch is independently shippable; build + test verification and a commit follow each.

## Roadmap / privacy note

`ROADMAP-V8-IMPLEMENTATION.md` Phase F is updated to record that the assistant ships as a
**cloud (Claude API) intent path** — opt-in, BYO-key — with the local Phi-3/ONNX runtime preserved
as a future fully-offline option. Documented: the only data leaving the machine is the user's
messages plus a short system-metrics summary, sent to Anthropic under the user's own API key.

## Out of scope (follow-ups)

- Migrating `ApiHostService`'s hand-mapped REST endpoints onto the command registry.
- Windows speech-to-text voice input (roadmap F4) — text + omnibox first.
- Server-proxied key custody (designed so a proxy backend could swap in later without UI changes).
- On-device local-LLM fallback.
