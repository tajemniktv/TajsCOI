param(
    [int] $Tail = 120,
    [string] $Pattern = "TajsTweaks|ERROR|Exception|WARN|Warning",
    [switch] $All
)

$ErrorActionPreference = "Stop"
$logDir = Join-Path $env:APPDATA "Captain of Industry\Logs"

if (-not (Test-Path $logDir))
{
    throw "Captain of Industry log directory does not exist: $logDir"
}

$log = Get-ChildItem $logDir -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $log)
{
    throw "No log files found in: $logDir"
}

Write-Host "Following: $($log.FullName)"
if (-not $All)
{
    Write-Host "Filter: $Pattern"
    Write-Host "Use -All to show every line."
}
Write-Host ""

Get-Content $log.FullName -Tail $Tail -Wait | ForEach-Object {
    $line = $_

    if (-not $All -and $line -notmatch $Pattern)
    {
        return
    }

    if ($line -match "ERROR|Exception|Fatal")
    {
        Write-Host $line -ForegroundColor Red
    }
    elseif ($line -match "WARN|Warning")
    {
        Write-Host $line -ForegroundColor Yellow
    }
    elseif ($line -match "TajsTweaks")
    {
        Write-Host $line -ForegroundColor Cyan
    }
    else
    {
        Write-Host $line
    }
}
