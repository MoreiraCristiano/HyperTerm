<p align="center">
  <img src="assets/hyperterm_minimal.svg" width="128" alt="HyperTerm logo">
</p>

<h1 align="center">HyperTerm</h1>

<p align="center">
  A Windows terminal and SSH session manager built with .NET, Avalonia, and xterm.js.
</p>

HyperTerm combines local terminal profiles, saved SSH connections, and optional
persistent psmux sessions in one focused desktop application. PowerShell is the
recommended default, while custom profiles can launch other shells and tools.

## Preview

### Split panes

Run independent terminals side by side inside the same tab. Panes support
horizontal and vertical layouts, draggable dividers, directional keyboard
navigation, and a subtle active-pane indicator.

![Two independent PowerShell terminals split inside one HyperTerm tab](docs/prints/split%20panes.png)

### SSH session manager

Search and sort saved SSH connections, edit connection details, and assign
sessions to folders without storing passwords or other credentials.

![HyperTerm SSH session manager with session details and folder organization](docs/prints/ssh%20manager.png)

### Local terminal profiles

Choose the default shell and configure its executable, arguments, and starting
directory. Additional profiles can launch other local shells and tools.

![HyperTerm settings showing the local terminal profile editor](docs/prints/profiles.png)

## Highlights

- Configurable local terminal profiles with a selectable default
- Saved SSH sessions organized in nested folders
- Multiple isolated tabs with editable titles
- Recursive horizontal and vertical split panes with independent terminals
- Persistent psmux sessions with attach, detach, and shutdown controls
- Terminal output search and command palette
- Configurable font, cursor, and selection appearance
- WebGL-accelerated xterm.js renderer with DOM fallback
- Local SQLite storage with no remote telemetry

HyperTerm uses the installed Windows OpenSSH client and never stores passwords
or other SSH credentials.

## Requirements

- Windows 10 or Windows 11
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) or newer
- [Node.js](https://nodejs.org/) with npm
- Microsoft Edge WebView2 Runtime
- PowerShell for the recommended default profile
- Windows OpenSSH Client (`ssh.exe`) for SSH sessions

[psmux](https://github.com/psmux/psmux) is bundled in complete release packages.
Development builds can also resolve `psmux.exe` from `PATH`.

## Run from source

From the repository root:

```powershell
.\scripts\bootstrap.ps1
```

This installs web terminal dependencies, builds HyperTerm in Release mode, and
starts the generated executable. To build without launching:

```powershell
.\scripts\bootstrap.ps1 -BuildOnly
```

## Build a release

Create a self-contained Windows release ZIP:

```powershell
.\scripts\build.ps1
```

Output is written under `artifacts\releases\`. The package includes the .NET
runtime, native libraries, web terminal assets, and verified psmux binary.

## Verify changes

Run the local quality gate:

```powershell
.\scripts\verify.ps1
```

Use `-Package` to also build and validate the release ZIP, or `-Coverage` to run
the coverage-enforcing checks used by CI.

## Local data

HyperTerm stores its data under `%LocalAppData%\HyperTerm\`:

```text
hyperterm.db   Saved sessions and folders
settings.json Application, profile, and appearance settings
```

Settings writes are atomic, imported archives are validated before mutation,
and diagnostics remain local without capturing terminal input or credentials.

## Architecture

```text
Avalonia UI -> one WebView2 per tab -> xterm.js pane instances
            -> C# bridge -> independent ConPTY sessions -> shell / ssh.exe
```

The solution follows a simplified Clean Architecture split across
`HyperTerm.Core`, `HyperTerm.Infrastructure`, and `HyperTerm.UI`. See the
[architecture guide](docs/architecture.md) for project boundaries and terminal
lifecycle details.

## Technology

.NET 10, Avalonia UI 12, CommunityToolkit.Mvvm, Entity Framework Core, SQLite,
Porta.Pty, Windows ConPTY, WebView2, xterm.js, PowerShell, OpenSSH, and psmux.
