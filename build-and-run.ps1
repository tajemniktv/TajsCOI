param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build.ps1"

& $buildScript -Configuration $Configuration -Run
exit $LASTEXITCODE
