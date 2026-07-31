[CmdletBinding()]
param(
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\SuperTerminal.UI\SuperTerminal.UI.csproj'
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
$activeProcessFile = Join-Path $repositoryRoot 'artifacts\active-run.pid'

function Stop-PreviousBootstrapRun {
    if (-not (Test-Path -LiteralPath $activeProcessFile)) {
        return
    }

    $previousProcessId = 0
    $storedValue = (Get-Content -LiteralPath $activeProcessFile -Raw).Trim()
    if (-not [int]::TryParse($storedValue, [ref]$previousProcessId)) {
        Remove-Item -LiteralPath $activeProcessFile -Force
        return
    }

    $previousProcess = Get-Process -Id $previousProcessId -ErrorAction SilentlyContinue
    if ($null -eq $previousProcess) {
        Remove-Item -LiteralPath $activeProcessFile -Force
        return
    }

    $expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\runs'))
    $processPath = $previousProcess.Path
    $isWorkspaceBuild = $processPath -and
        $previousProcess.ProcessName -eq 'SuperTerminal.UI' -and
        [System.IO.Path]::GetFullPath($processPath).StartsWith(
            $expectedRoot,
            [System.StringComparison]::OrdinalIgnoreCase)

    if ($isWorkspaceBuild) {
        Write-Host "Fechando execução anterior (PID $previousProcessId)..."
        Stop-Process -Id $previousProcessId -Force -ErrorAction SilentlyContinue
        $previousProcess.WaitForExit(5000)
    }

    Remove-Item -LiteralPath $activeProcessFile -Force
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK não encontrado.'
    }

    $dotnetPath = $dotnetCommand.Source
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$outputPath = Join-Path $repositoryRoot "artifacts\runs\$runId"
$webTerminalPath = Join-Path $repositoryRoot 'src\SuperTerminal.UI\WebTerminal'

Write-Host 'Sincronizando dependências do terminal web...'
& npm.cmd install --prefix $webTerminalPath --no-audit --no-fund
if ($LASTEXITCODE -ne 0) {
    throw "Instalação web falhou com código $LASTEXITCODE."
}

Write-Host 'Compilando terminal xterm.js...'
& npm.cmd run build --prefix $webTerminalPath
if ($LASTEXITCODE -ne 0) {
    throw "Build web falhou com código $LASTEXITCODE."
}

Write-Host 'Compilando SuperTerminal atual...'
& $dotnetPath build $projectPath `
    --configuration Release `
    --output $outputPath `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Compilação falhou com código $LASTEXITCODE."
}

$executablePath = Join-Path $outputPath 'SuperTerminal.UI.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Executável não foi gerado: $executablePath"
}

Write-Host "Build atual: $executablePath"

if (-not $BuildOnly) {
    Stop-PreviousBootstrapRun
    Write-Host 'Abrindo SuperTerminal...'
    $applicationProcess = Start-Process `
        -FilePath $executablePath `
        -WorkingDirectory $outputPath `
        -PassThru
    Set-Content -LiteralPath $activeProcessFile -Value $applicationProcess.Id
}
