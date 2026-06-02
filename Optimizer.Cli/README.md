# Optimizer CLI

Command-line interface to the Optimizer desktop app. All commands communicate
with the running GUI via the built-in REST API (added in Batch 41).

## Prerequisites

1. Start the **Optimizer GUI**
2. Open **Settings → Remote API**, enable the API, and copy your token

## Setup

```powershell
$env:OPTIMIZER_TOKEN = "your-token-here"
$env:OPTIMIZER_URL   = "http://localhost:8765"   # optional — this is the default
```

## Commands

| Command | Description |
|---------|-------------|
| `optimizer status` | Show CPU, memory, GPU, disk usage and sensor temps |
| `optimizer profile list` | List all available profiles |
| `optimizer apply <profile-id>` | Apply an optimization profile |
| `optimizer scan` | Run a diagnostics scan and show recommendations |
| `optimizer cleanup` | Clear temporary files |

## Examples

```powershell
# Check current resource usage
optimizer status

# List profiles, then apply one
optimizer profile list
optimizer apply preset-gaming

# Scan for issues
optimizer scan

# Clean temp files
optimizer cleanup
```

## Build

```powershell
cd L:\Projects
dotnet build Optimizer.Cli/Optimizer.Cli.csproj -c Release
# Binary produced at: Optimizer.Cli/bin/Release/net10.0/optimizer.exe
```

## Notes

- `OPTIMIZER_TOKEN` is **required** — the API rejects requests without a valid token
- If the GUI is not running you will see "Connection failed" — there is no offline fallback
- The CLI targets `net10.0` (no `-windows` suffix) and can be run on any platform
  where the .NET 10 runtime is installed, though the GUI itself is Windows-only
