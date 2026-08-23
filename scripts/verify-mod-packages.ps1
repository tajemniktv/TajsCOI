param(
    [Parameter(Mandatory = $true)]
    [string] $ModsRoot
)

$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path -LiteralPath $ModsRoot).Path
$expectedDlls = [ordered]@{
    TajsCore = @('0Harmony.dll', 'TajsCOI.Common.dll', 'TajsCore.dll')
    TajsTweaks = @('TajsTweaks.dll')
    TajsProfiler = @('TajsProfiler.dll')
    TajsPerformance = @('TajsPerformance.dll')
}

foreach ($entry in $expectedDlls.GetEnumerator()) {
    $modId = $entry.Key
    $modRoot = Join-Path $resolvedRoot $modId
    $manifestPath = Join-Path $modRoot 'manifest.json'

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Missing manifest for $modId at '$manifestPath'."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $primaryDlls = @($manifest.primary_dlls)
    $expected = @($entry.Value)

    if (($primaryDlls -join '|') -cne ($expected -join '|')) {
        throw "$modId primary_dlls order mismatch. Expected '$($expected -join ', ')', got '$($primaryDlls -join ', ')'."
    }

    foreach ($primaryDll in $primaryDlls) {
        $primaryPath = Join-Path $modRoot $primaryDll
        if (-not (Test-Path -LiteralPath $primaryPath -PathType Leaf)) {
            throw "$modId declares missing primary DLL '$primaryDll'."
        }
    }

    $actualDlls = @(Get-ChildItem -LiteralPath $modRoot -Filter '*.dll' -File |
        Sort-Object Name |
        ForEach-Object Name)
    $expectedSorted = @($expected | Sort-Object)
    if (($actualDlls -join '|') -cne ($expectedSorted -join '|')) {
        throw "$modId DLL contents mismatch. Expected '$($expectedSorted -join ', ')', got '$($actualDlls -join ', ')'."
    }
}

Write-Host "Verified deterministic mod packages under '$resolvedRoot'."
