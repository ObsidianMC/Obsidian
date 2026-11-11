# Sleeping and spawn points


**Status:** mostly done. Players sleep in beds, skip the night by `players_sleeping_percentage`, and respawn at
their bed or charged respawn anchor, saved in vanilla's `respawn` field (see `PlayerBase.Sleeping`).

- Unlike vanilla's, a respawn anchor next to water explodes like any other.
- Waking and respawning at a bed or straw bed stand the player up by vanilla's
  `AbstractBedBlock.findStandUpPosition` (`BedBlocks.StandUpPosition`); a respawn anchor still uses Obsidian's own
  first free spot around it, not vanilla's `RespawnAnchorBlock.findStandUpPosition` order.
- `/spawnpoint` doesn't set a forced respawn point yet, and villagers in a bed don't keep players out of it.
