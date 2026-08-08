# HyperTerm testing

The suite is split by layer so fast deterministic failures remain easy to locate:

- `HyperTerm.Core.Tests`: validation, domain services, folder rules and architecture.
- `HyperTerm.Infrastructure.Tests`: SQLite, settings, process and ConPTY integration.
- `HyperTerm.UI.Tests`: view models, bridge/buffer tests and Avalonia Headless controls.
- `HyperTerm.E2E.Tests`: published desktop application through Windows UI Automation.
- `HyperTerm.TestTerminal`: deterministic console helper for lifecycle scenarios.

Run the normal suite with `verify.ps1`. Generate coverage with
`eng\coverage.ps1`; add `-Enforce` to apply the repository thresholds. Run mutation
with `eng\mutation.ps1 -WebTerminal`.

Coverage excludes generated XAML, EF migrations and native/platform wrappers; those
are exercised by integration and E2E tests instead. Current baselines block
regressions immediately. The verifier also reports the ratchet targets: Core
90/85, deterministic Infrastructure 85/80, and UI logic 80/75 (line/branch).

Desktop tests require a dedicated interactive Windows 11 x64 runner with an
unlocked session, WebView2 Evergreen, 1920x1080 resolution, 100% scale and no SSH
credentials. Set `HYPERTERM_RUN_E2E=1` and point `HYPERTERM_E2E_APP` to a published
`HyperTerm.exe`. Tests create an isolated data directory through
`HYPERTERM_TEST_MODE=1`; production launches ignore `HYPERTERM_DATA_ROOT`.

Stress tests are opt-in through `HYPERTERM_RUN_STRESS=1`. Every async wait must use
an observable event/task and a finite timeout. Do not introduce arbitrary sleeps or
automatic retries. A flaky test may only be quarantined with a tracked issue,
owner and removal date.
