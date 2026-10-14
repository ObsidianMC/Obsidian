# Engine sample

A plugin that uses what each phase of [the engine plan](../../docs/engine-foundations.md) adds, through the public API
alone (`Obsidian.API`, and `Obsidian.Nbt` for saved data), as a mod would. It's documentation by example, and a smoke
test: `Obsidian.Tests/SamplePlugin.cs` packs its build output (laid out like its `.obby`: the assembly,
`obsidian.mod.json` and `data/`) and loads it through the plugin manager, one test per phase.

| Phase | Content |
| --- | --- |
| 1, blocks that do things | `sample:tick_counter` ([TickCounter](TickCounter)): faces the player who places it, turns on and lights up a scheduled tick after redstone powers it, counts the ticks it's on in its block entity (saved, synced to watching players, read by comparators), has a 12-pixel shape from its block entity, and drops itself through `data/sample/loot_table/blocks/tick_counter.json`. |

Later phases add to the same plugin: a machine block, its screen, a cable block, a portal and a mob.

See [plugin-content.md](../../docs/plugin-content.md) for the API it uses.
