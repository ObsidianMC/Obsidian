# Engine sample

A mod that uses what each phase of [the engine plan](../../docs/engine-foundations.md) adds, through the public API
alone (`Obsidian.API` and `Obsidian.API.Client`, and `Obsidian.Nbt` for saved data and component values). Its side is
`both`: servers run its `ConfigureRegistry`, and the Obsidian client runs it too, then its `ConfigureClient`. It's
documentation by example, and a smoke test: `Obsidian.Tests/SamplePlugin.cs` packs its build output (laid out like its
`.obby`: the assembly, `obsidian.mod.json`, `data/` and `assets/`) and loads it through the plugin manager, and the
client's tests load it as a client mod.

| Phase | Content |
| --- | --- |
| 1, blocks that do things | `sample:tick_counter` ([TickCounter](TickCounter)): faces the player who places it, turns on and lights up a scheduled tick after redstone powers it, counts the ticks it's on in its block entity (saved, synced to watching players, read by comparators), has a 12-pixel shape from its block entity, and drops itself through `data/sample/loot_table/blocks/tick_counter.json`, keeping its count on its item in a server-side data component, `sample:ticks` (its block entity's `CollectComponents`, copied by the loot table's `copy_components`), which placing it again reads back (`ApplyComponents`). |
| 2, items, inventories and recipes | `sample:grinder` ([Grinder](Grinder)): a machine whose block entity is a `ContainerBlockEntity` with an input slot and an output slot, and saves how far it got grinding. Each tick it pulls an item from the container above through `ILevel.GetItemAccess` (only what its input takes), grinds it by its own recipe type, `sample:grinding` (`data/sample/recipe/grinding/`: iron, gold and copper ore into their raw metal, and cobblestone into gravel), found through its block entity's `Server`, the plugin's own, and pushes the results into whatever is below, planning each move on both ends first so that nothing is lost or made twice. The results carry a server-side data component, `sample:ground_by`, with the grinder's position. Its sides give other blocks the input and its bottom the output, so hoppers feed and empty it too. |
| 3, menus and screens | The grinder's menu, `sample:grinder` (`GrinderMenu`), which using it opens: a `MenuContainer` bound to its block entity, with its input and output slots (the output take-only), data slots for its progress, its recipe's time and its mode, a button turning pulling from above on and off (saved with the block entity), and its recipes, listed with `IServer.GetRecipes` and written as the menu's open data. Its client side registers `GrinderView.razor`, a screen built in Razor (the project uses the Razor SDK) and drawn with vanilla's GUI textures: the furnace's arrow as a progress bar, the mode button, and a search field filtering a scrolling `ItemGrid` of the recipes. `GrinderScreen`, a `ModMenuScreen`, is the same screen built in code. Its texts are in `assets/sample/lang/en_us.json`. |
| 4, rendering | The grinder's spinning wheel, a cable whose model follows its neighbours, a moth to ride with glowing eyes, and the tick counter's sounds: see [below](#phase-4-rendering). |

### Phase 4, rendering

The client draws the sample's blocks and its mob from `assets/sample/`, with vanilla's block textures where it can.

- **The grinder's wheel.** The grinder's block entity tells clients when it starts and stops grinding:
  `working_since`, the level's game time when it started, in its update data (`GetUpdateData`, sent with
  `SendUpdate`). Its client side registers a block entity renderer whose provider draws the wheel,
  `models/part/grinder_wheel.json`, on top of the block's model (`models/block/grinder.json`), turning at
  `Speed` degrees a tick from that tick (`Since`): the client spins it each frame without calling the mod.
- **`sample:cable`** ([Cable](Cable)): connects to cables and to blocks holding items (the grinder, chests,
  hoppers) beside it, in a boolean block state property for each side, worked out as a player places it
  (`GetStateForPlacement`) and as its neighbours change (`UpdateShape`), like a fence. Its multipart blockstate,
  `blockstates/cable.json`, adds an arm to its core for each side that connects, so it needs neither a block entity
  nor client code. Its collision and outline are still a full block (see
  [known-gaps.md](../../docs/known-gaps.md#plugin-block-shapes-by-state)).
- **`sample:moth`** ([Moth](Moth)): a flying mob whose behaviour's `CanMount` lets players ride it, with a seat on
  its back (`PassengerAttachments`). Its client side draws it with its own model layer,
  `models/entity/moth.json`, and texture, `textures/entity/moth.png`, and its eyes with an emissive layer,
  `moth_eyes.png`, transparent but for the eyes, which glow in the dark.
- **Sounds.** `sample:counter.chime` in the tick counter's placement and break sound type. Its `sounds.json`
  references vanilla's amethyst chime, so the sample needs no audio file. The tick counter's smoke test checks
  these sounds; the client API's positional and UI playback are exercised by the client test probe mod.

`Obsidian.Tests/SamplePlugin.cs` checks that the grinder says when it grinds, that cables follow what's placed
and removed beside them and that players ride the moth; the client's `EngineSampleShowsItsGrinderScreen` checks
the renderers the sample registers, the moth's model and the cable's models for its states.

Later phases add to the same mod: a portal, and saved data and a loot table for the moth.

See [plugin-content.md](../../docs/plugin-content.md) for the API it uses.
