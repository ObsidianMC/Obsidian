# Entities lost when an unloading save fails

**Status:** open; entities can be lost when a chunk's entity write fails as it unloads.

When a chunk unloads, its save snapshot collects the chunk's entities (`Region.CollectEntities`, called from
`SnapshotChunkAsync`). That snapshot also takes each saved entity out of the region and the entity grid and marks the
chunk's entities unloaded (`Chunk.EntitiesUnloaded`). All of this happens before the entity region file is written.

If that write then fails, `CompleteSaveAsync` only sets `SavedEntityCount` to -1, and the chunk stays loaded for
another unloading save. By then the entities are no longer in the level, and the chunk says its entities are unloaded,
so `CollectEntities` returns nothing on the retry. The entities from the failed write are lost. Block data doesn't
have this problem, because a failed chunk write marks the chunk unsaved again (`save.Taken?.MarkUnsaved()`).

Every unloading save takes this path, including the batched unloads that pregeneration has used since #673. The loss
only happens when a write fails, for example on a full disk or an I/O error.

**Where to start:** keep the collected entity NBT on the save until its write has succeeded. On failure, put it back on
the chunk, for example as pending entities, and clear `EntitiesUnloaded` so the retry writes it. Cover it with a test
that makes the entity region write fail on unload, then checks that the entities are still saved by the next flush.
