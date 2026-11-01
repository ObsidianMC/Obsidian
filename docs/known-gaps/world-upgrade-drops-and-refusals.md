# Moving pistons dropped by the world upgrade

**Status:** chosen limit of the 1.21.11 world upgrade.

A block being pushed or pulled when a 1.21.11 world was last saved is a moving piston (`minecraft:moving_piston`),
whose block entity holds the block it carries. Obsidian 1.21.11 saved that block as a numeric 1.21.11 block state id
(`MovedState`). Only 1.21.11's state table can decode those ids, and it was removed with the rest of the 1.21.11 data,
so the upgrade (`LegacyChunkUpgrade`) drops these moving pistons: the moving piston becomes air, with its block entity
gone, and the block it carried is lost. Moving pistons in upgraded structure templates become air the same way. Only
blocks that were mid-move in the instant of the save are affected; pistons and their heads stay.

Palette entries that Obsidian saved before its vanilla-format saves, as a 1.21.11 state id beside a name, keep the
named block with the properties saved beside it, or the block's default state when none were.

**Where to start:** keeping the moved blocks needs 1.21.11's block state names by id (29,671 entries, from the 1.21.11
server's `blocks.json` report), shipped or generated for the upgrade alone, and read where `LegacyChunkUpgrade` drops
the moving piston, to write 26.3's `blockState`, `facing`, `extending`, `source` and `progress` fields instead. Keep the
upgrade backup if those blocks matter.
