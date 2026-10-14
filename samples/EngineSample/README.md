# Engine sample

A plugin that uses what each phase of [the engine plan](../../docs/engine-foundations.md) adds, through the public API
alone (`Obsidian.API`, and `Obsidian.Nbt` for saved data and component values), as a mod would. It's documentation by example, and a smoke
test: `Obsidian.Tests/SamplePlugin.cs` packs its build output (laid out like its `.obby`: the assembly,
`obsidian.mod.json` and `data/`) and loads it through the plugin manager, one test per phase.

| Phase | Content |
| --- | --- |
| 1, blocks that do things | `sample:tick_counter` ([TickCounter](TickCounter)): faces the player who places it, turns on and lights up a scheduled tick after redstone powers it, counts the ticks it's on in its block entity (saved, synced to watching players, read by comparators), has a 12-pixel shape from its block entity, and drops itself through `data/sample/loot_table/blocks/tick_counter.json`, keeping its count on its item in a server-side data component, `sample:ticks` (its block entity's `CollectComponents`, copied by the loot table's `copy_components`), which placing it again reads back (`ApplyComponents`). |
| 2, items, inventories and recipes | `sample:grinder` ([Grinder](Grinder)): a machine whose block entity is a `ContainerBlockEntity` with an input slot and an output slot, and saves how far it got grinding. Each tick it pulls an item from the container above through `ILevel.GetItemAccess` (only what its input takes), grinds it by its own recipe type, `sample:grinding` (`data/sample/recipe/grinding/iron_ore.json`: an iron ore into two raw iron over 20 ticks), found through its block entity's `Server`, the plugin's own, and pushes the results into whatever is below, planning each move on both ends first so that nothing is lost or made twice. The results carry a server-side data component, `sample:ground_by`, with the grinder's position. Its sides give other blocks the input and its bottom the output, so hoppers feed and empty it too. |

Later phases add to the same plugin: the grinder's screen, a cable block, a portal and a mob.

Phase 4 sounds add `sample:counter.chime` to the counter's placement and break sound type. Its
`assets/sample/sounds.json` references vanilla's amethyst chime, so the sample needs no extra audio file. The
placement/break and reload smoke test also checks these sounds. The client API's positional and UI playback are
exercised by the client test probe mod.

See [plugin-content.md](../../docs/plugin-content.md) for the API it uses.
