# Player break effects


**Status:** open. Found by the `block_use` captures that break a block after using it (`breakAfterUse`); both are
named expected differences in `VanillaBlockUse`, pinned to their exact cases, which fail once Obsidian matches
vanilla: `BreakSoundForLevelEvent2001` (the six `*_destroy*` cases) and `DestroyEventAfterRemoval`
(`jukebox_destroy_with_disc`).

- **Level event 2001.** A player breaking a block sends the players around it the block's break sound
  (`BlockSoundPlayback`), where vanilla's `playerWillDestroy` sends level event 2001 with the state, whose clients
  play that sound and show the break particles. Other players see no particles.
- **Order of game events.** The break emits `block_destroy` once the block is gone, after the game events its removal
  emits (a jukebox's `jukebox_stop_play`); vanilla's `playerWillDestroy` emits it before the removal.

**Where to start:** `MainEventHandler.BreakBlockAsync` (`Obsidian/Events/MainEventHandler.World.cs`), which removes
the block and then plays the sound and emits the game event.
