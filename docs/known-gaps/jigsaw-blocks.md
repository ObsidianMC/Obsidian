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

Chunk generation is asynchronous in Obsidian. Generate assembles pieces, then evaluates placement against a
private block/entity overlay. A missing chunk suspends planning before live writes; Generate loads just that
chunk outside world locks and retries with the same placement random seed. It rechecks the original block
entity, settings and orientation before each retry and the live commit. Changing the editor while waiting
cancels the stale placement. Vanilla performs this work synchronously on its server thread.

Chunk demand comes from actual template/processor accesses, including the conditional waterlogging search,
not declared template sizes or piece boxes. Jigsaw pool placement uses known-shape flags in vanilla 26.3, so
the template's final shape and neighbour-update passes are skipped. The capture observes native FULL-chunk
requests: a dry slab beside water across a chunk boundary requests that neighbour and becomes waterlogged;
a sparse 96 by 96 template requests only its entries' chunk. Cold-chunk replay checks the generated chunk set
as well as block/entity output. Existing fixture cases remain unpinned.

The planning view does not run block-removal, on-place, fluid or plugin callbacks; those run during live
placement. Their additional accesses are not preloaded, and callbacks that change blocks can make the live
world differ from the overlay. No speculative padding is generated to cover them. Exact callback-dependent
chunk demand needs a resumable live update path, which this change does not introduce. Features and processors
reuse the worldgen implementation; their parity is not newly established by these rigid-template captures.
Planning can evaluate them repeatedly, so stateful plugin processors must not assume a single evaluation.
An unloaded biome query cancels placement rather than generating terrain: vanilla can satisfy such a query at
BIOMES status, while Obsidian's live chunk lease currently loads FULL. This deliberately leaves unsupported
biome-dependent placement incomplete instead of generating terrain the query does not justify.

Placement randomness uses a fresh seed shared by planning retries and the live commit. These fixtures use
deterministic templates rather than pinning a vanilla level-random state; processor/entity randomness is not
newly established by this capture.
