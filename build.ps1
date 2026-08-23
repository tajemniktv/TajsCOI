param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [switch] $Run
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "TajsCOI.slnx"
$mods = Get-ChildItem (Join-Path $root "src\Mods") -Filter "*.csproj" -Recurse | ForEach-Object {
    [xml] $projectXml = Get-Content $_.FullName
    [pscustomobject] @{
        Id = @($projectXml.Project.PropertyGroup.ModId)[0]
        Version = @($projectXml.Project.PropertyGroup.ModVersion)[0]
    }
} | Where-Object { $_.Id }

Write-Host "Building Taj's COI mods ($Configuration)..."
dotnet build $solution -c $Configuration

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Deployed:"
$mods | ForEach-Object {
    Write-Host "  $env:APPDATA\Captain of Industry\Mods\$($_.Id)"
}

if ($Configuration -eq "Release")
{
    Write-Host "Release package:"
    $mods | ForEach-Object {
        Write-Host "  $env:APPDATA\Captain of Industry\Mods\$($_.Id)_$($_.Version).zip"
    }
}

if ($Run)
{
    Write-Host ""
    Write-Host "Launching Captain of Industry through Steam..."
    Start-Process "steam://rungameid/1594320"
}
