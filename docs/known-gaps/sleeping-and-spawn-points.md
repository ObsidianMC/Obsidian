# Sleeping and spawn points


**Status:** mostly done. Players sleep in beds, skip the night by `players_sleeping_percentage`, and respawn at
their bed or charged respawn anchor, saved in vanilla's `respawn` field (see `PlayerBase.Sleeping`).

- Unlike vanilla's, a respawn anchor next to water explodes like any other.
- Waking stands the player up on the first free spot around the bed, not by vanilla's
  `BedBlock.findStandUpPosition` order.
- `/spawnpoint` doesn't set a forced respawn point yet, and villagers in a bed don't keep players out of it.
