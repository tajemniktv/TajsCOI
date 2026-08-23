# Streaming save compression proxy benchmark (2026-08-23)

This benchmark isolates the compression sink used by issue #5. It compares the
vanilla-shaped `MemoryStream` staging approach with gzip written directly to a
seekable `FileStream`. It does not replace the required in-game save, checksum,
reload, and peak-process-memory validation.

## Input and method

- Latest local COI save selected by `scripts/benchmark-save-compression.ps1`.
- Existing 40-byte save header removed and the gzip payload decompressed once.
- Uncompressed payload: 77,674,283 bytes.
- Five compression rounds per sink, using `CompressionLevel.Optimal`.
- Full collections were requested before each round so the reported managed
  delta primarily represents memory retained by that compression sink.
- CRC scans, header writing, post-write validation, and atomic rename were not
  included. Both sinks ran from the same in-memory source payload and warm OS
  cache.

## Results

| Sink | Compressed bytes | Median time | Median managed delta |
| --- | ---: | ---: | ---: |
| `MemoryStream` | 36,420,858 | 1,108.01 ms | 134,169,640 bytes (127.95 MiB) |
| Direct `FileStream` | 36,420,858 | 1,157.95 ms | 105,504 bytes (0.10 MiB) |

Direct streaming removed about 127.85 MiB of managed compression-buffer growth
in this proxy while taking about 4.5% longer. Equal compressed sizes confirm
that both benchmark paths consumed the same payload and compression settings;
the repository's round-trip and CRC tests cover the actual streaming writer's
header and integrity behavior.

## Decision

Keep `StreamingSaveCompression` opt-in. The proxy supports proceeding to an
in-game A/B capture with `TajsProfiler`, but acceptance still requires saving
and reloading representative games, post-write checksum success, corruption
detection, and observed process-memory and duration results.

Reproduce with:

```powershell
./scripts/benchmark-save-compression.ps1 -Rounds 5
```
