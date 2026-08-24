param(
    [string]$SavePath,
    [ValidateRange(1, 15)]
    [int]$Rounds = 3
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SavePath)) {
    $saveRoot = Join-Path $env:APPDATA 'Captain of Industry\Saves'
    $SavePath = Get-ChildItem -LiteralPath $saveRoot -Recurse -Filter '*.save' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$resolvedSave = (Resolve-Path -LiteralPath $SavePath).Path
$saveStream = [System.IO.File]::OpenRead($resolvedSave)
try {
    $saveStream.Position = 40
    $gzip = [System.IO.Compression.GZipStream]::new(
        $saveStream,
        [System.IO.Compression.CompressionMode]::Decompress,
        $true)
    try {
        $payloadStream = [System.IO.MemoryStream]::new()
        try {
            $gzip.CopyTo($payloadStream)
            $payload = $payloadStream.ToArray()
        }
        finally {
            $payloadStream.Dispose()
        }
    }
    finally {
        $gzip.Dispose()
    }
}
finally {
    $saveStream.Dispose()
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($round in 0..$Rounds) {
    foreach ($mode in @('MemoryStream', 'FileStream')) {
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()
        $beforeBytes = [GC]::GetTotalMemory($true)
        $timer = [System.Diagnostics.Stopwatch]::StartNew()

        if ($mode -eq 'MemoryStream') {
            $output = [System.IO.MemoryStream]::new(65536)
            try {
                $compressor = [System.IO.Compression.GZipStream]::new(
                    $output,
                    [System.IO.Compression.CompressionLevel]::Optimal,
                    $true)
                try {
                    $compressor.Write($payload, 0, $payload.Length)
                }
                finally {
                    $compressor.Dispose()
                }
                $compressedBytes = $output.Length
                $afterBytes = [GC]::GetTotalMemory($false)
            }
            finally {
                $output.Dispose()
            }
        }
        else {
            $temporaryPath = Join-Path ([System.IO.Path]::GetTempPath()) (
                'tajs-save-compression-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            try {
                $output = [System.IO.FileStream]::new(
                    $temporaryPath,
                    [System.IO.FileMode]::CreateNew,
                    [System.IO.FileAccess]::Write,
                    [System.IO.FileShare]::None,
                    65536,
                    [System.IO.FileOptions]::SequentialScan)
                try {
                    $compressor = [System.IO.Compression.GZipStream]::new(
                        $output,
                        [System.IO.Compression.CompressionLevel]::Optimal,
                        $true)
                    try {
                        $compressor.Write($payload, 0, $payload.Length)
                    }
                    finally {
                        $compressor.Dispose()
                    }
                    $compressedBytes = $output.Length
                }
                finally {
                    $output.Dispose()
                }
                $afterBytes = [GC]::GetTotalMemory($false)
            }
            finally {
                if ([System.IO.File]::Exists($temporaryPath)) {
                    [System.IO.File]::Delete($temporaryPath)
                }
            }
        }

        $timer.Stop()
        if ($round -gt 0) {
            $results.Add([pscustomobject]@{
                Mode = $mode
                Round = $round
                Milliseconds = $timer.Elapsed.TotalMilliseconds
                ManagedDeltaBytes = $afterBytes - $beforeBytes
                CompressedBytes = $compressedBytes
            })
        }
    }
}

function Get-Median([object[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) {
        return $sorted[$middle]
    }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$summary = foreach ($mode in @('MemoryStream', 'FileStream')) {
    $modeResults = @($results | Where-Object Mode -eq $mode)
    $times = @($modeResults.Milliseconds | Sort-Object)
    $deltas = @($modeResults.ManagedDeltaBytes | Sort-Object)
    [pscustomobject]@{
        Mode = $mode
        Rounds = $Rounds
        PayloadBytes = $payload.LongLength
        CompressedBytes = $modeResults[0].CompressedBytes
        MedianMilliseconds = [Math]::Round((Get-Median $times), 2)
        MedianManagedDeltaBytes = Get-Median $deltas
    }
}

$summary | Format-Table -AutoSize
