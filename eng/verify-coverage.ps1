[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
[xml]$report = Get-Content -LiteralPath $ReportPath
$thresholds = @{
    # Baselines are enforced immediately. Targets are reported and ratcheted upward.
    'HyperTerm.Core' = @{ Line = 90; Branch = 78; TargetLine = 90; TargetBranch = 85 }
    'HyperTerm.Infrastructure' = @{ Line = 85; Branch = 70; TargetLine = 85; TargetBranch = 80 }
    'HyperTerm' = @{ Line = 60; Branch = 40; TargetLine = 80; TargetBranch = 75 }
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($assemblyName in $thresholds.Keys) {
    $package = @($report.coverage.packages.package) |
        Where-Object { $_.name -eq $assemblyName } |
        Select-Object -First 1
    if ($null -eq $package) {
        $failures.Add("Coverage package '$assemblyName' is missing.")
        continue
    }

    $line = [Math]::Round(([double]$package.'line-rate') * 100, 2)
    $branch = [Math]::Round(([double]$package.'branch-rate') * 100, 2)
    $required = $thresholds[$assemblyName]
    Write-Host "$assemblyName line=$line% branch=$branch%"
    if ($line -lt $required.Line) {
        $failures.Add("$assemblyName line coverage $line% is below $($required.Line)%.")
    }
    if ($branch -lt $required.Branch) {
        $failures.Add("$assemblyName branch coverage $branch% is below $($required.Branch)%.")
    }
    if ($line -lt $required.TargetLine -or $branch -lt $required.TargetBranch) {
        Write-Warning "$assemblyName has not reached its target ($($required.TargetLine)% line, $($required.TargetBranch)% branch)."
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
