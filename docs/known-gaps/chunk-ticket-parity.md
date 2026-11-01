# Chunk ticket parity


**Status:** plugin tickets implemented; vanilla ticket propagation and persistence deferred.

`ILevel.AddChunkTicket` keeps a square ticking with one loaded border, rather than vanilla's propagated levels:
31 entity ticking, 32 block ticking and 33 full. The border is loaded-only; there is no separate block-ticking
ring. Natural spawning still needs players. Plugin tickets are bounded, automatically released on unload and not
saved; a mod can store coordinates with `RegisterSavedData` and reacquire handles when it starts.

Vanilla 1.21.11's `TicketType.FORCED` persists and loads/simulates without a timeout; `PORTAL` expires after 300
ticks, while `PLAYER_SPAWN` is a temporary loading ticket. `TicketStorage.TYPE` saves persistent ticket entries in
the level's `data/chunks.dat`, including forced chunks changed by `/forceload`; this is no longer the older
`ForcedChunks` long array in `chunks.dat`. Obsidian does not import those tickets or provide `/forceload` yet.

**Where to start:** capture a 1.21.11 forced-ticket save and replay it through an engine-owned saved-data codec;
add `/forceload` and the separate block-ticking border when a concrete user needs vanilla interoperability.
