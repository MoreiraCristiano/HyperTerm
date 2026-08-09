# AGENTS.md

## Project overview

HyperTerm is a Windows-only terminal and SSH session manager built with .NET,
Avalonia, WebView2, xterm.js, ConPTY, PowerShell, OpenSSH, SQLite, and optional
psmux sessions.

The solution follows a simplified Clean Architecture:

- `HyperTerm.Core`: entities, models, validation, service contracts, and domain services.
- `HyperTerm.Infrastructure`: SQLite, settings, logging, process resolution, PTY, SSH, and psmux implementations.
- `HyperTerm.UI`: Avalonia views, view models, platform services, WebView bridge, and terminal frontend.
- `tests/HyperTerm.Tests`: unit and integration tests.

## Product constraints

- Support Windows 10 and Windows 11 only.
- Preserve compatibility with existing SQLite databases, `settings.json`, session archives, keyboard shortcuts, and release layout unless the task explicitly requires a migration.
- Never store passwords or other SSH credentials.
- SSH must continue to use the installed Windows OpenSSH client.
- psmux sessions must remain isolated in the `hyperterm` namespace.
- Keep all telemetry local. Do not add remote analytics or crash reporting without explicit approval.

## Required workflow

1. Read relevant contracts, implementation, and tests before editing.
2. Check `git status --short`; preserve unrelated user changes.
3. Make the smallest cohesive change that fixes the underlying problem.
4. Add or update regression tests for changed behavior.
5. Build the web terminal when files under `src/HyperTerm.UI/WebTerminal` change.
6. Run the proportional validation commands below.
7. Report changed behavior, validation results, and any remaining risk.

Do not replace the PTY backend, persistence format, UI framework, or architecture
to solve a localized bug without concrete evidence and explicit user approval.

## Build and validation

From repository root:

```powershell
# Web terminal
npm.cmd ci --prefix .\src\HyperTerm.UI\WebTerminal --no-audit --no-fund
npm.cmd run build --prefix .\src\HyperTerm.UI\WebTerminal

# .NET build and tests
dotnet build .\HyperTerm.sln --configuration Release
dotnet test .\HyperTerm.sln --configuration Release --no-build

# Complete distributable package
.\scripts\build.ps1
```

Use `npm.cmd run build` after JavaScript, HTML, xterm.js, or frontend build changes.
Run the complete test suite after changes to Core, Infrastructure, shared terminal
contracts, persistence, application lifecycle, or shared view models.

## Coding standards

- Follow repository `.NET` settings: nullable enabled and warnings treated as errors.
- Prefer explicit, readable names over abbreviations.
- Keep Core independent from Infrastructure and UI.
- Put operating-system and external-process details in Infrastructure or UI platform services.
- Use dependency injection; avoid service locators and mutable global state.
- Pass `CancellationToken` through asynchronous call chains.
- Avoid unobserved tasks. `async void` is allowed only for framework event handlers; delegate work to an observed `Task` and handle failures.
- Make cleanup and shutdown idempotent. Treat process exit, pipe closure, cancellation, and disposal as concurrent events.
- Never block the Avalonia UI thread with process, database, filesystem, or terminal I/O.
- Marshal UI-bound property and collection changes to the Avalonia dispatcher when callbacks originate from PTY or background threads.
- Catch only exceptions that can be handled locally. Preserve cancellation semantics and log actionable context.
- Do not log terminal input, private-key contents, credentials, or sensitive environment values.
- Keep persisted comments, documentation, and user-facing text in normal professional language.

## Terminal pipeline rules

The pipeline is:

```text
Avalonia UI -> WebView2 -> xterm.js -> C# bridge -> Porta.Pty/ConPTY -> PowerShell -> child process
```

- Preserve raw terminal input unless implementing a documented application shortcut.
- Do not special-case a terminal application when a protocol or lifecycle fix is possible.
- Validate every WebView message before using its payload.
- Bound queued output and preserve backpressure; never allow unlimited terminal output growth.
- A closed PTY pipe after process exit is expected lifecycle behavior, not an application-fatal error.
- Serialize writes against disposal and ignore writes after terminal completion.
- Unsubscribe events before disposing PTY, WebView, or tab resources.
- Keep one terminal buffer and one PTY session per tab.
- Preserve the DOM renderer fallback when WebGL initialization or context recovery fails.

## Persistence and security rules

- Apply database changes through EF Core migrations; never edit an existing migration after release.
- Make settings writes atomic through a temporary file in the same directory followed by replacement.
- Validate imported archives before mutation and apply changes transactionally.
- Keep archive size and item-count limits enforced for seekable and non-seekable streams.
- Quote PowerShell and process arguments using the existing centralized helpers; never concatenate untrusted values into executable command text without escaping.
- Resolve executables through the existing resolver and return actionable launch errors.
- Verify hashes for downloaded release dependencies.

## Testing expectations

Add regression coverage near the affected layer:

- Core: validation, folder rules, archive compatibility, and service behavior.
- Infrastructure: repositories, settings, process resolution, psmux, PTY lifecycle, and failure handling.
- UI/view models: commands, selection, tab lifecycle, cancellation, and state transitions.
- Web terminal: message validation, input forwarding, resize, output completion, shortcuts, and renderer fallback.

PTY tests should cover normal exit, output split across UTF-8 byte boundaries, writes
during exit, repeated dispose, cancellation, resize after exit, and high-volume output.
Avoid arbitrary delays in tests; synchronize with events, tasks, or explicit state.

## Change boundaries

- Do not modify generated `bin`, `obj`, `dist`, release, staging, cache, database, or local settings artifacts directly.
- Do not commit files under `artifacts/`.
- Do not update dependencies opportunistically inside unrelated fixes.
- Do not alter user-visible behavior, stored data, or packaging silently.
- Keep changes reviewable and grouped by concern; avoid broad formatting churn.
