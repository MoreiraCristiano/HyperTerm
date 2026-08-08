[CmdletBinding()]
param(
    [switch]$Enforce
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resultsRoot = Join-Path $repositoryRoot 'artifacts\test-results'
$reportRoot = Join-Path $repositoryRoot 'artifacts\coverage'
$projects = @(
    'tests\HyperTerm.Core.Tests\HyperTerm.Core.Tests.csproj',
    'tests\HyperTerm.Infrastructure.Tests\HyperTerm.Infrastructure.Tests.csproj',
    'tests\HyperTerm.UI.Tests\HyperTerm.UI.Tests.csproj',
    'tests\HyperTerm.Tests\HyperTerm.Tests.csproj'
)

if (Test-Path -LiteralPath $resultsRoot) {
    Remove-Item -LiteralPath $resultsRoot -Recurse -Force
}
if (Test-Path -LiteralPath $reportRoot) {
    Remove-Item -LiteralPath $reportRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resultsRoot, $reportRoot | Out-Null

foreach ($project in $projects) {
    & dotnet test (Join-Path $repositoryRoot $project) `
        --configuration Release `
        --no-build `
        --collect 'XPlat Code Coverage' `
        --results-directory $resultsRoot `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage test run failed for $project."
    }
}

$coverageFiles = @(Get-ChildItem -Path $resultsRoot -Recurse -Filter 'coverage.cobertura.xml')
if ($coverageFiles.Count -eq 0) {
    throw 'No Cobertura reports were produced.'
}

$reports = ($coverageFiles.FullName -join ';')
& dotnet reportgenerator `
    "-reports:$reports" `
    "-targetdir:$reportRoot" `
    '-reporttypes:Html;Cobertura;JsonSummary' `
    '-assemblyfilters:+HyperTerm.Core;+HyperTerm.Infrastructure;+HyperTerm' `
    '-classfilters:-CompiledAvaloniaXaml.*;-HyperTerm.UI.App;-HyperTerm.UI.Program;-HyperTerm.UI.Views.*;-HyperTerm.UI.Controls.WebTerminalHostControl;-HyperTerm.UI.Services.AvaloniaSystemFontService;-HyperTerm.UI.Services.AvaloniaThemeService;-HyperTerm.UI.Services.ExecutableFilePicker;-HyperTerm.UI.Services.LogInteractionService;-HyperTerm.UI.Services.SessionArchiveFilePicker;-HyperTerm.UI.Services.WindowsWebViewFocus;-HyperTerm.Infrastructure.Persistence.Migrations.*;-HyperTerm.Infrastructure.Persistence.HyperTermDbContextFactory;-HyperTerm.Infrastructure.Storage.ApplicationPathProvider;-HyperTerm.Infrastructure.Terminal.PortaPtySessionFactory;-HyperTerm.Infrastructure.Terminal.PowerShellSessionFactory;-HyperTerm.Infrastructure.Terminal.PsmuxService;-HyperTerm.Infrastructure.Terminal.WindowsExecutableResolver'
if ($LASTEXITCODE -ne 0) {
    throw 'Coverage report generation failed.'
}

if ($Enforce) {
    & (Join-Path $PSScriptRoot 'verify-coverage.ps1') `
        -ReportPath (Join-Path $reportRoot 'Cobertura.xml')
}
