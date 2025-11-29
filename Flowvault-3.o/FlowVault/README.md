# Flow Vault

A WinUI 3 desktop application featuring a glassmorphic floating overlay bar with anchored translucent tiles, AI chat integration, background indexing, and task management.

## Features

- **Glassmorphic UI**: Acrylic/Mica overlay bar and translucent tiles
- **Global Assistant**: AI chat with streaming responses in every tile
- **Task Management**: Priority scoring, auto-scheduling, workflow graphs
- **File Indexer**: Background incremental file indexing with summaries
- **Click-Through**: Toggle overlay transparency with Ctrl+.
- **Secure Storage**: DPAPI-encrypted API keys, SQLite persistence
- **Pinned Tiles**: Persist tile positions across sessions

## Architecture

```
FlowVault/
├── src/
│   ├── FlowVault.UI/           # WinUI 3 frontend
│   ├── FlowVault.BackendHost/  # Background worker service
│   └── FlowVault.Shared/       # Shared DTOs and contracts
└── tests/
    └── FlowVault.Tests/        # Unit and integration tests
```

## Requirements

- Windows 10 version 1809 or higher
- .NET 8.0 SDK
- Windows App SDK 1.5+
- Visual Studio 2022 (recommended)

## Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `FLOW_VAULT_GEMINI_API_KEY` | Gemini API key for AI features | Optional |
| `FLOW_VAULT_OPENAI_API_KEY` | OpenAI API key (alternative) | Optional |
| `FLOW_VAULT_BACKEND_IPC_MODE` | IPC mode: `namedpipe` or `grpc` | Optional (default: namedpipe) |

## Getting Started

### Development

```powershell
# Build and run both backend and UI
.\tooling\start-dev.ps1

# Run tests
.\tooling\run-tests.ps1
```

### Manual Build

```powershell
# Restore and build
dotnet restore
dotnet build

# Run backend host
cd src\FlowVault.BackendHost
dotnet run

# Run UI (separate terminal)
cd src\FlowVault.UI
dotnet run
```

## UI Controls

### Top Bar (56px)
- **Left**: Project, Files, Tasks, Chat, Calendar, AI, Overflow icons
- **Right**: Opacity slider, Click-Through toggle, Notifications, Settings

### Keyboard Shortcuts
- `Ctrl+.` - Toggle click-through mode
- `Ctrl+Shift+A` - Open Global Assistant
- `Ctrl+Shift+S` - Open Settings
- `Escape` - Close active tile

### Tile Behavior
- Tiles anchor below the clicked toolbar button
- Max height: min(720px, 60% screen height)
- Vertical expansion with smooth animation
- Internal scrollbar when content exceeds max height

## API Adapters

### Mock Adapter (Default)
Used for local development and testing. Simulates streaming responses.

### Gemini Adapter
Set `FLOW_VAULT_GEMINI_API_KEY` environment variable.

### OpenAI Adapter
Set `FLOW_VAULT_OPENAI_API_KEY` environment variable.

## Database

SQLite database stored in `%LOCALAPPDATA%\FlowVault\flowvault.db`

Tables:
- `Projects` - Indexed project configurations
- `Tasks` - Task items with priorities
- `FileSummaries` - Indexed file metadata
- `FolderSummaries` - Aggregated folder data
- `ChatMessages` - Conversation history
- `PinnedTiles` - Persisted tile positions
- `ApiKeys` - Encrypted API credentials

## Testing

```powershell
dotnet test tests/FlowVault.Tests/
```

Test coverage:
- `IndexerTests` - File indexing and summary generation
- `SchedulerTests` - Auto-scheduling algorithm
- `PriorityTests` - Task priority scoring
- `LlmMockStreamingTests` - Streaming token delivery
- `PinPersistenceTest` - Tile position persistence

## License

MIT License
