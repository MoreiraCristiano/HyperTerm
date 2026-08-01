# HyperTerm

HyperTerm is a modern Windows terminal and SSH session manager built with .NET and Avalonia. It provides a developer-tool interface for organizing connections while using PowerShell 7, Windows OpenSSH, ConPTY, and xterm.js for the actual terminal experience.

> HyperTerm is under active MVP development. It does not implement the SSH protocol and does not store passwords. SSH connections are executed by the Windows OpenSSH client inside PowerShell 7.

## Features

- Local PowerShell 7 terminal opened automatically at startup
- SSH session management with host, port, username, private key, folder, and notes
- Nested folders with mouse-driven creation, editing, deletion, and session drag-and-drop
- Multiple terminal tabs with isolated processes and editable titles
- Shared WebView terminal host optimized for multiple concurrent tabs
- WebGL-accelerated xterm.js renderer with safe DOM fallback
- Native Windows clipboard integration
- Resizable and collapsible session sidebar
- Configurable terminal font, font size, cursor, blinking, and selection color
- Dark-only interface inspired by modern developer tools
- SQLite persistence and JSON application settings

## Requirements

- Windows 10 or Windows 11
- [.NET SDK 9](https://dotnet.microsoft.com/download/dotnet/9.0) or newer
- [Node.js](https://nodejs.org/) with npm
- [PowerShell 7](https://github.com/PowerShell/PowerShell) (`pwsh.exe`)
- Windows OpenSSH Client (`ssh.exe`) for SSH sessions
- Microsoft Edge WebView2 Runtime

HyperTerm targets `net9.0`. A newer installed SDK, including .NET 10, can build the project as long as it supports that target.

## Quick start

From the repository root, run:

```powershell
.\bootstrap.ps1
```

The bootstrap script:

1. Installs the web terminal dependencies.
2. Builds the xterm.js bundle.
3. Builds HyperTerm in Release mode.
4. Starts the newly generated executable.

Each run receives an isolated output directory under `artifacts/runs/`. The script also closes the previous instance that it started, preventing stale development builds from accumulating.

To build without launching the application:

```powershell
.\bootstrap.ps1 -BuildOnly
```

## Manual build

Build the web terminal first because its generated `dist` directory is intentionally excluded from Git:

```powershell
npm install --prefix .\src\HyperTerm.UI\WebTerminal --no-audit --no-fund
npm run build --prefix .\src\HyperTerm.UI\WebTerminal
dotnet build .\src\HyperTerm.UI\HyperTerm.UI.csproj --configuration Release
```

The executable is named `HyperTerm.exe`.

## First run

HyperTerm resolves `pwsh.exe` from `PATH` and then checks the standard PowerShell 7 installation directory. To select another installation:

1. Open **Settings**.
2. Under **Shell**, select the desired `pwsh.exe` with the Windows file picker.
3. Save the settings.

SSH sessions require the Windows OpenSSH Client. HyperTerm launches `ssh.exe` through the configured PowerShell 7 executable and leaves authentication prompts inside the terminal.

## Keyboard shortcuts

Application shortcuts use `Ctrl+Shift` where possible so regular `Ctrl` combinations remain available to terminal applications.

| Action | Shortcut |
| --- | --- |
| Toggle sidebar | `Ctrl+Shift+B` |
| Create session | `Ctrl+Shift+N` |
| Open selected session | `Ctrl+Shift+O` |
| Edit selected session | `F2` |
| Close active tab | `Ctrl+Shift+W` |
| Open settings | `Ctrl+Shift+,` |
| Show shortcuts | `F1` |
| Copy terminal selection | `Ctrl+Shift+C` |
| Paste into terminal | `Ctrl+Shift+V` |
| Close active non-terminal screen | `Esc` |

Double-click a saved session to open it. Double-click a terminal tab to rename it.

## Data storage

User data is stored locally under:

```text
%LocalAppData%\HyperTerm\
├── hyperterm.db
└── settings.json
```

- `hyperterm.db` stores sessions and folders in SQLite.
- `settings.json` stores the PowerShell path, terminal appearance, and window state.

Existing data from the previous `hyperTerms` or `SuperTerminal` application directories is copied automatically when the current files do not yet exist.

## Architecture

The solution follows a simplified Clean Architecture structure:

```text
HyperTerm.sln
└── src
    ├── HyperTerm.Core
    ├── HyperTerm.Infrastructure
    ├── HyperTerm.Shared
    └── HyperTerm.UI
```

- **Core** contains entities, models, service contracts, and terminal abstractions.
- **Infrastructure** implements SQLite, settings, PowerShell/OpenSSH launching, and ConPTY sessions.
- **UI** contains Avalonia views, MVVM view models, the design system, and the xterm.js host.
- **Shared** contains cross-project utilities.

The product, solution, projects, assemblies, and namespaces use the final **HyperTerm** name consistently.

## Terminal pipeline

```text
HyperTerm UI → shared WebView2 host → xterm.js → C# bridge → ConPTY → pwsh.exe → ssh.exe
```

Each tab owns an independent ConPTY process and xterm buffer. A shared WebView2 host reduces memory usage, while bounded output queues and backpressure keep high-volume terminals from freezing the UI.

## Technology

- C# and .NET 9
- Avalonia UI 12
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting and dependency injection
- Entity Framework Core 9 with SQLite
- Porta.Pty and Windows ConPTY
- xterm.js 5 with WebGL
- PowerShell 7 and Windows OpenSSH

## Current MVP boundaries

- Windows only
- Dark theme only
- SSH depends on the locally installed Windows OpenSSH client
- No password or credential storage
- No built-in SSH implementation
- No terminal session restoration after restarting the application
