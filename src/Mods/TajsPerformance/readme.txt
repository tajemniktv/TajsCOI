Taj's Performance

Evidence-backed, individually switchable performance fixes for Captain of Industry.

Features are disabled by default and configured independently.

Available opt-in features:
- Large save/load read buffer: changes BlobReader's 4 KiB buffer to a configurable 16-256 KiB
  buffer (64 KiB default). It preserves checksum preflight and all vanilla load semantics.
- Streaming save compression: writes gzip directly into CoI's seekable temporary file, patches
  the fixed header afterward, and preserves compressed CRC, post-write validation, and atomic rename.
  The uncompressed CRC is retained unless its separate option is explicitly enabled.
- Low product textures: applies mip bias 3 (Low) or 4 (Very Low) through the normal product
  texture-array rebuild. Vanilla presets and CoI's 64 px minimum remain unchanged.
- Manual asset trim: exposes paused-only trim_unused_assets and trim_unused_assets_status
  commands. It clears only CoI's reloadable AssetsDb cache and invokes Unity's normal unused-
  asset unload operation; it is never scheduled automatically.
- Product buffer shrink: after a configurable sustained observation window at or below 25%
  utilization, releases only the live/reserve instance buffers and lets CoI's normal dirty-path
  upload recreate them with power-of-two sizing. Stable owner and slot buffers are never compacted.

Local benchmark evidence for the default came from five read-only decompression passes over a
35.3 MB save (77.7 MB expanded): median 327 ms at 4 KiB, 223 ms at 16 KiB, 213 ms at 64 KiB,
and 213 ms at 256 KiB. Full in-game load A/B validation is still required before enabling it by default.
