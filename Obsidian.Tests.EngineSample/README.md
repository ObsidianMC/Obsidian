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
| 5, world, players and entities | A portal to the sample's dimension, the moth's saved rides and its loot, and charm slots each player keeps: see [below](#phase-5-world-players-and-entities). |

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
  nor client code. Its `StateShapes` give each state the same core and arms to collide with and aim at, on the
  server and the client alike.
- **`sample:moth`** ([Moth](Moth)): a flying mob that players ride by using it, with a seat on its back
  (`PassengerAttachments`), and a spawn egg in the sample's creative tab. Its client side draws it with its own
  model layer, `models/entity/moth.json`, and texture, `textures/entity/moth.png`, and its eyes with an emissive
  layer, `moth_eyes.png`, transparent but for the eyes, which glow in the dark.
- **Items and the tick counter's model.** `items/` holds an item model definition for each block's item: the
  grinder's draws its block model and its wheel together, the cable's its own model (`models/item/cable.json`, a
  core and a straight run), and the tick counter's its block model, which `blockstates/tick_counter.json` draws
  with a lamp on top that lights while it's powered. The block models inherit `models/block/base.json`, the display
  transforms of vanilla's `block/block`, so the items look like vanilla's block items in hand and in inventories.
- **Sounds.** `sample:counter.chime` in the tick counter's placement and break sound type. Its `sounds.json`
  references vanilla's amethyst chime, so the sample needs no audio file. The tick counter's smoke test checks
  these sounds; the client API's positional and UI playback are exercised by the client test probe mod.

`Obsidian.Tests/SamplePlugin.cs` checks that the grinder says when it grinds, that cables follow what's placed
and removed beside them, with shapes to match, and that players ride the moth; the client's
`EngineSampleShowsItsGrinderScreen` checks the renderers the sample registers, the moth's model, the blocks' and
items' models, and the cable's models and shapes for its states.

### Phase 5, world, players and entities

- **`sample:dusk`**: a dimension in data alone. `data/sample/dimension/dusk.json` is a flat level of end stone over
  bedrock, and its type, `data/sample/dimension_type/dusk.json`, gives it the End's sky and a violet fog, which
  clients draw from the type's data, so the client side needs no code for it. The server creates its level as the
  plugin loads.
- **The dusk portal** ([Portal](Portal)). `DuskPortal` registers a portal type with `RegisterPortal`: a frame of
  amethyst blocks, at least 2 by 3 inside, lit by using an amethyst shard on it, fills with `sample:dusk_portal`,
  which takes what stands in it to the same place in the dusk, or back to the overworld, building a frame there if
  none is near. Its blockstate draws vanilla's Nether portal models, so it ships no texture. The client keeps only
  the portal type's identity.
- **The moth's rides and loot.** The moth counts the times players get on it in an attachment,
  `sample:moth_rides`, saved with it: its behaviour's `InteractAsync` seats the player with `StartRiding` and
  counts, where a behaviour with nothing to count returns true from `CanMount`. Killed by a player, it drops a
  `sample:charm` through `data/sample/loot_table/entities/moth.json` and its `minecraft:killed_by_player` condition.
- **Charm slots** ([Charms](Charms)): two slots each player keeps in an item attachment, `sample:charms`
  (`ItemAttachments.Codec`), saved with the player and dropped at a death like the inventory. Pressing K on the
  Obsidian client (the `sample:charms` key binding) sends an `OpenCharms` payload, whose server handler opens
  `CharmMenu`: the slots next to the player's inventory, each taking one charm and nothing else. Its screen,
  `CharmsView.razor`, is drawn with vanilla's GUI textures. Each worn charm grants a jump in the air
  (`MovementAbilities.ExtraJumps`), granted again when the attachment's `Changed` event says the charms changed
  and as a player joins. The charm's item model draws vanilla's heart of the sea.

`Obsidian.Tests/SamplePlugin.PortalAndCharms.cs` lights the portal and travels through it to the dusk, then opens
the charm slots with the payload, wears a charm (dirt isn't taken) and takes it off, checking the jump it grants.
`SamplePlugin.cs` checks the moth's rides after a save and load, and its charm drop. The client's
`EngineSampleShowsItsGrinderScreen` checks the charm key binding and the charm menu's screen.

See [plugin-content.md](../../docs/plugin-content.md) for the API it uses.
