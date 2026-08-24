# Save/load read-buffer benchmark — 2026-08-23

This is a read-only microbenchmark supporting the opt-in `SaveLoadReadBuffer` feature. It does not replace the required in-game load A/B test.

## Input and method

- Captain of Industry 0.8.7a save
- Compressed file size: 35,346,103 bytes
- Decompressed payload: 77,674,283 bytes
- Five warm-cache decompression passes per buffer size
- The save header was skipped and the gzip payload was read to completion through .NET's `GZipStream`
- Reported values are minimum, median, and maximum wall time

This isolates the read-call granularity affected by `BlobReader`'s `BufferedReadStream`. It does not include checksum preflight, object deserialization, resolver finalization, scene initialization, or cleanup GC.

| Buffer | Minimum | Median | Maximum |
|---:|---:|---:|---:|
| 4 KiB | 311.57 ms | 327.00 ms | 372.27 ms |
| 16 KiB | 206.60 ms | 223.42 ms | 230.30 ms |
| 64 KiB | 193.91 ms | 212.82 ms | 218.64 ms |
| 256 KiB | 196.03 ms | 213.03 ms | 218.08 ms |

## Decision

Use 64 KiB as the default for the opt-in feature:

- it reduced median decompression time by roughly 35% in this microbenchmark;
- 256 KiB showed no meaningful improvement over 64 KiB;
- the extra per-reader allocation remains small;
- vanilla checksum preflight and all load/deserialization behavior remain unchanged.

The feature remains disabled by default until representative in-game captures confirm that this microbenchmark improvement materially reduces whole-load time without a regression.
