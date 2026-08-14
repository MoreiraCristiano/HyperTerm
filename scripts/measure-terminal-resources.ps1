param(
    [string]$Scenario = "manual",
    [int]$Samples = 5,
    [int]$IntervalMilliseconds = 500
)

$root = Get-CimInstance Win32_Process |
    Where-Object { $_.Name -eq "HyperTerm.exe" } |
    Select-Object -First 1
if ($null -eq $root) {
    throw "HyperTerm.exe is not running."
}

$rows = for ($sample = 1; $sample -le $Samples; $sample++) {
    $all = Get-CimInstance Win32_Process
    $ids = [System.Collections.Generic.HashSet[uint32]]::new()
    [void]$ids.Add([uint32]$root.ProcessId)
    do {
        $added = $false
        foreach ($process in $all) {
            if ($ids.Contains([uint32]$process.ParentProcessId) -and
                $ids.Add([uint32]$process.ProcessId)) {
                $added = $true
            }
        }
    } while ($added)

    $live = foreach ($process in $all) {
        if ($ids.Contains([uint32]$process.ProcessId)) {
            Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        }
    }

    [pscustomobject]@{
        Scenario = $Scenario
        Sample = $sample
        WorkingSetMB = [math]::Round((($live | Measure-Object WorkingSet64 -Sum).Sum / 1MB), 1)
        PrivateMB = [math]::Round((($live | Measure-Object PrivateMemorySize64 -Sum).Sum / 1MB), 1)
        CpuSeconds = [math]::Round((($live | Measure-Object CPU -Sum).Sum), 2)
        Processes = @($live).Count
        WebViewProcesses = @($live | Where-Object ProcessName -eq "msedgewebview2").Count
        Handles = ($live | Measure-Object HandleCount -Sum).Sum
        Threads = ($live | ForEach-Object Threads | Measure-Object).Count
    }

    if ($sample -lt $Samples) {
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}

$rows | Format-Table -AutoSize
