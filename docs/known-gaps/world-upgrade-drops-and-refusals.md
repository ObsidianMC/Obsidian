# World upgrade drops and refusals

**Status:** chosen limit of the 1.21.11 world upgrade.

A block being pushed or pulled when a 1.21.11 world was last saved is a moving piston (`minecraft:moving_piston`),
whose block entity holds the block it carries. Obsidian 1.21.11 saved that block as a numeric 1.21.11 block state id
(`MovedState`), in every build, including those that save chunks in vanilla's format. Only 1.21.11's state table can
decode those ids, and it was removed with the rest of the 1.21.11 data, so the upgrade (`LegacyChunkUpgrade`) drops
these moving pistons: the moving piston becomes air, with its block entity gone, and the block it carried is lost.
Moving pistons in upgraded structure templates become air the same way. Only blocks that were mid-move in the instant
of the save are affected; pistons and their heads stay.

Worlds that Obsidian saved before its vanilla-format saves (#539) aren't upgraded at all. Their chunk palettes name
every block state by a 1.21.11 state id beside the block's name, without its properties, so without the state table
every stair, door, slab and fluid level would lose its state. The upgrade checks every level's chunks first (what it
would convert; not what a level keeps in its `data` folder), and refuses such a world with an error naming it,
leaving it unchanged apart from `session.lock` (no backup, nothing moved). It's a load failure, not a crash. A world
counts as that format when any chunk section's block-state palette entry carries an `Id` tag, which only that writer
produced. The same check refuses a world with a chunk, region file or template the upgrade couldn't read, naming the
file, so a world is never left half-converted.

**Where to start:** keeping the moved blocks, or upgrading pre-#539 worlds, needs 1.21.11's block state names by id
(29,671 entries, from the 1.21.11 server's `blocks.json` report), shipped or generated for the upgrade alone and read
where `LegacyChunkUpgrade` drops the moving piston or refuses the palette. Opening a pre-#539 world with a later
1.21.x build doesn't help: it only rewrites the chunks it loads.
