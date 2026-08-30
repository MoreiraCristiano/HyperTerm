# Scripts

Run these commands from the repository root:

```powershell
.\scripts\bootstrap.ps1                 # Build and launch a development run
.\scripts\bootstrap.ps1 -BuildOnly      # Build without launching
.\scripts\build.ps1                     # Create the x64 ZIP and per-user installer
.\scripts\build.ps1 -Runtime win-arm64  # Create the ARM64 portable ZIP
.\scripts\test.ps1                      # Run all .NET tests
.\scripts\test.ps1 -Filter Category=ConPty
.\scripts\verify.ps1                    # Run the complete local quality gate
.\scripts\verify.ps1 -Coverage          # Run the CI-equivalent gate with coverage
.\scripts\coverage.ps1 -Enforce         # Generate and enforce coverage
.\scripts\mutation.ps1 -WebTerminal     # Run .NET and JavaScript mutation
.\scripts\web-terminal.ps1              # Restore, test, and build the frontend
.\scripts\reset-data.ps1                # Remove local application data safely
```
