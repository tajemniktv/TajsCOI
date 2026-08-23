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
$input = [System.IO.File]::OpenRead($resolvedSave)
try {
    $input.Position = 40
    $gzip = [System.IO.Compression.GZipStream]::new(
        $input,
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
    $input.Dispose()
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($mode in @('MemoryStream', 'FileStream')) {
    foreach ($round in 1..$Rounds) {
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
        $results.Add([pscustomobject]@{
            Mode = $mode
            Round = $round
            Milliseconds = $timer.Elapsed.TotalMilliseconds
            ManagedDeltaBytes = $afterBytes - $beforeBytes
            CompressedBytes = $compressedBytes
        })
    }
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
        MedianMilliseconds = [Math]::Round($times[[Math]::Floor($times.Count / 2)], 2)
        MedianManagedDeltaBytes = $deltas[[Math]::Floor($deltas.Count / 2)]
    }
}

$summary | Format-Table -AutoSize
