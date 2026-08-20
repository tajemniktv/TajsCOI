param(
    [int] $Tail = 120,
    [string] $Pattern = "TajsTweaks|ERROR|Exception|Fatal|WARN|Warning",
    [switch] $All
)

$ErrorActionPreference = "Stop"
$logDir = Join-Path $env:APPDATA "Captain of Industry\Logs"
$logFilePattern = '^\d{2}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_[A-Za-z0-9]{4}\.log$'

if (-not (Test-Path $logDir))
{
    throw "Captain of Industry log directory does not exist: $logDir"
}

$log = Get-ChildItem $logDir -File -Filter "*.log" |
    Where-Object { $_.Name -match $logFilePattern } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $log)
{
    throw "No Captain of Industry log files matching YY-MM-DD_HH-mm-ss_XXXX.log found in: $logDir"
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
