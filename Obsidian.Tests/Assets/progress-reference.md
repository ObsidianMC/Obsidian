# Progress fixture provenance

Captured from the pinned Minecraft 1.21.11 server jar using its packet stream codecs and save codecs, in UTC.
Jar SHA-256: `ec47239a8de246335e1d54f6ac319bd35641778eb4b6a6da06372840d02fcebc`.
Mappings: the matching `server.txt` described in the common engine brief.

- `progress-stats.json`: `ServerStatsCounter` saved after setting custom `play_time = 1200`, `jump = 2`.
- `progress-stats.bin`: `ClientboundAwardStatsPacket` payload, same values, in insertion order (no packet id).
- `progress-advancement.json`: `AdvancementProgress.CODEC` for requirements
  `[["first", "alternate"], ["second"]]`, with `first` achieved at `2026-01-02T03:04:05Z`.
- `progress-advancements.bin`: `ClientboundUpdateAdvancementsPacket` payload: reset, holder `probe:progress`,
  no parent/display, the requirements above, telemetry true, no removals, the captured progress,
  and showAdvancements false. Criterion-map iteration in this capture is alternate, first, second.

The capture helper constructs public vanilla value objects, sets the criterion instant to the fixed time via
reflection, and invokes the packet codecs with a RegistryFriendlyByteBuf over the built-in registry access.
It uses bootstrap/version initialization and the pinned jar's runtime libraries. Save output comes from the
vanilla codecs; fixtures contain no decompiled source. The advancement test checks AND-of-OR requirements
independently of the codec's `done` flag.
