param(
    [Parameter(Mandatory = $true)]
    [string] $ModsRoot
)

$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path -LiteralPath $ModsRoot).Path
$expectedPrimaryDlls = [ordered]@{
    TajsCore = @('0Harmony.dll', 'TajsCOI.Common.dll', 'TajsBootstrap.dll', 'TajsCore.dll')
    TajsTweaks = @('TajsTweaks.dll')
    TajsProfiler = @('TajsProfiler.dll')
    TajsPerformance = @('TajsPerformance.dll')
}

$expectedPackageDlls = [ordered]@{
    TajsCore = @('0Harmony.dll', 'TajsCOI.Common.dll', 'TajsBootstrap.dll', 'TajsCore.dll', 'winhttp.dll')
    TajsTweaks = @('TajsTweaks.dll')
    TajsProfiler = @('TajsProfiler.dll')
    TajsPerformance = @('TajsPerformance.dll')
}

foreach ($entry in $expectedPackageDlls.GetEnumerator()) {
    $modId = $entry.Key
    $modRoot = Join-Path $resolvedRoot $modId
    $manifestPath = Join-Path $modRoot 'manifest.json'

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Missing manifest for $modId at '$manifestPath'."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.id -cne $modId) {
        throw "$modId manifest ID mismatch. Expected '$modId', got '$($manifest.id)'."
    }
    $primaryDlls = @($manifest.primary_dlls)
    $expectedPrimary = @($expectedPrimaryDlls[$modId])
    $expectedPackage = @($entry.Value)

    if (($primaryDlls -join '|') -cne ($expectedPrimary -join '|')) {
        throw "$modId primary_dlls order mismatch. Expected '$($expectedPrimary -join ', ')', got '$($primaryDlls -join ', ')'."
    }

    foreach ($primaryDll in $primaryDlls) {
        $primaryPath = Join-Path $modRoot $primaryDll
        if (-not (Test-Path -LiteralPath $primaryPath -PathType Leaf)) {
            throw "$modId declares missing primary DLL '$primaryDll'."
        }
    }

    $actualDlls = @(Get-ChildItem -LiteralPath $modRoot -Filter '*.dll' -File -Recurse |
        ForEach-Object { $_.FullName.Substring($modRoot.Length).TrimStart('\', '/').Replace('\', '/') } |
        Sort-Object)
    $expectedSorted = @($expectedPackage | Sort-Object)
    if (($actualDlls -join '|') -cne ($expectedSorted -join '|')) {
        throw "$modId DLL contents mismatch. Expected '$($expectedSorted -join ', ')', got '$($actualDlls -join ', ')'."
    }
}

Write-Host "Verified deterministic mod packages under '$resolvedRoot'."
