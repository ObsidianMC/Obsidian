# Jigsaw blocks

Status: editor settings, block entity persistence/update data and Generate are implemented against vanilla 26.3.
The focused `jigsaw_blocks.json` capture replays every case without output pins.

`SetJigsawBlock` requires creative mode and permission level 2. It replaces all seven editor fields together,
marks the block entity changed and sends its custom saved data to tracking clients. Pool identifiers are retained
even when no such pool exists. Neither packet clamps priorities or generation levels. An unknown packet joint
becomes `aligned`; a fresh entity starts `rollable`, while a missing or invalid saved joint defaults to `rollable`
on vertical fronts and `aligned` on horizontal fronts. Saved names are `name`, `target`, `pool`, `final_state`,
`joint`, `selection_priority` and `placement_priority`.
Stored components survive loading and editor changes; vanilla's custom-only update tag excludes them.
The region loader resolves legacy joint defaults from the chunk's block state before publishing the entity,
so a save before its first level tick retains the correct default.

Generate anchors the requested target one block ahead of the editor. It reuses world generation's pool selection,
rotation, collision, joint and priority handling, seeded from the world's seed and the anchor's chunk. Its search
distance is 128, with no expansion hack, heightmap projection, biome rejection or explicit dimension padding.
The keep-jigsaws flag reaches the existing template processors. Templates use generated structure files and the
same data pack/plugin overrides as world generation.
Zero and negative levels place no pieces, including the starting template.

Chunk generation is asynchronous in Obsidian. Generate assembles pieces before acquiring tickets for their
destination chunks, releases world locks while waiting, and rechecks the original block entity, settings and
orientation before writing. Changing the editor during that wait cancels the stale placement. Vanilla performs
this work synchronously on its server thread.

The capture covers deterministic rigid templates, legacy save defaults, both priorities, both joints, depth
boundaries, rejected permissions, missing targets/pools, empty pools, placement orientations and a chunk crossing.
It does not establish fresh parity for every built-in worldgen processor, terrain-matching pool, feature element
or spawned entity; those reuse the existing worldgen implementation and its separate capture coverage. In
particular, features or processors that read/write beyond their piece's bounding box may reach unloaded chunks;
the current live adapter does not request additional chunks on those accesses. A follow-up needs a bounded,
asynchronous access plan for those elements, with native captures of their chunk requests and writes.

Placement randomness comes from the live placement view. These fixtures use deterministic templates rather than
pinning a vanilla level-random state; processor/entity randomness is not newly established by this capture.
