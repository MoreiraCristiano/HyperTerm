# Architecture

HyperTerm uses projects as architectural boundaries and feature folders inside
those boundaries. Physical folders improve navigation; namespaces remain stable
so folder moves do not become API changes.

## Dependency direction

```text
HyperTerm.UI ───────┐
                   ├──> HyperTerm.Core ──> HyperTerm.Shared
Infrastructure ────┘
```

- `HyperTerm.Core` owns entities, models, contracts, validation, and domain
  services. It does not reference Infrastructure or UI.
- `HyperTerm.Infrastructure` owns Windows, SQLite, filesystem, external process,
  PTY, SSH, psmux, settings, and logging implementations.
- `HyperTerm.UI` owns Avalonia composition, view models, view interactions,
  platform UI services, and the WebView/xterm.js bridge.
- `HyperTerm.Shared` is reserved for utilities that genuinely cross project
  boundaries.

## Feature organization

Features are grouped below their owning layer:

```text
HyperTerm.Core/Services/Sessions
HyperTerm.Infrastructure/Terminal
├── Launching
├── Psmux
└── Pty
HyperTerm.UI
├── Controls/WebTerminal
├── ViewModels
│   ├── Sessions
│   ├── Settings
│   └── Workspace
└── Views/Interactions
```

Large binding-facing view models remain stable facades. Their partial files are
split by workflow, while reusable validation and coordination logic lives in
small independently tested types. The same rule applies to `MainWindow`: XAML
handlers stay on the partial window class, grouped by interaction domain.

## Terminal boundaries

The terminal pipeline remains:

```text
Avalonia → WebView2 → xterm.js → validated bridge message
         → IPtySession → Porta.Pty adapter → ConPTY → PowerShell/OpenSSH
```

Only the Infrastructure adapter knows Porta.Pty types. The session lifecycle,
UTF-8 decoding, output backpressure, and WebTerminal output coordination are
testable without creating native processes.

## Adding code

Place a type in the narrowest owning feature without crossing the project
dependency direction. Keep contracts in Core, Windows or external-system
details in Infrastructure or UI platform services, and UI orchestration in UI.
Avoid creating a shared abstraction until at least two layers genuinely need it.
