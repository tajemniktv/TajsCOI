param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [switch] $Run
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\Mods\TajsTweaks\TajsTweaks.csproj"

[xml] $projectXml = Get-Content $project
$modVersion = @($projectXml.Project.PropertyGroup.ModVersion)[0]

Write-Host "Building TajsTweaks $modVersion ($Configuration)..."
dotnet build $project -c $Configuration

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Deployed:"
Write-Host "  $env:APPDATA\Captain of Industry\Mods\TajsTweaks"

if ($Configuration -eq "Release")
{
    Write-Host "Release package:"
    Write-Host "  $env:APPDATA\Captain of Industry\Mods\TajsTweaks_$modVersion.zip"
}

if ($Run)
{
    Write-Host ""
    Write-Host "Launching Captain of Industry through Steam..."
    Start-Process "steam://rungameid/1594320"
}
