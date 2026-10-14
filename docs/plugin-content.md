# Plugin blocks, entities, tags and data

Plugins add blocks in code or from JSON files that Obsidian's source generator turns into code, add block entity
types, data component types, entity types, particle types, menu types, worldgen types and level generators in code,
and add tags, biomes, dimension types, damage types, worldgen features, biome modifiers, noise settings, loot tables
and dimensions with data pack files, like NeoForge and Fabric mods. Clients need the matching mod for blocks, block
entity types, data component types sent to clients, items, entity types, particle types and menus: NeoForge and
Fabric clients, and the Obsidian client, receive those registries and map the server's ids to their own. The mods
may load in any order. Biomes, dimension types and damage types are sent with their data, so vanilla clients can join
too. On the Obsidian client, a mod adds its client side in code (see [Client](#client)).

`samples/EngineSample` is a working plugin built on this API alone, which the tests load like any plugin: its tick
counter is a block with state, hooks, a block entity, redstone, light and a loot table together.

## Blocks in code

```csharp
public override void ConfigureRegistry(IPluginRegistry registry)
{
    var lamp = registry.RegisterBlock("ruby:lamp", new BlockProperties
    {
        StateProperties = [StateProperty.Bool("lit"), StateProperty.Int("power", 0, 3)],
        DefaultState = new Dictionary<string, string> { ["lit"] = "false" },
        Hardness = 0.5f,
        LightEmission = 7,
        Behavior = new LampBehavior()
    });

    var lit = lamp.With(lamp.DefaultState, "lit", "true");
}
```

The block's identifier is in the plugin's namespace (its id). Unless `Item` is null, an item with the same identifier
places the block and is its fallback drop when it has no loot table. `LightEmission` is every state's light level;
`StateLightEmission` gives each state its own from its property values, like vanilla's per-state light level
(`StateLightEmission = values => values["lit"] == "true" ? 15 : 0`). It's asked once per state as the block
registers, and changing to a state with another level relights the area (see [Runtime light](#runtime-light)). A
client mod declares the same properties and values in the same order: bool properties are `true, false`, int ones
ascending, enum ones in the order given.

Structures turn and mirror the block, in templates and pool elements, by its properties named like vanilla's: a
`facing` of `north`, `east`, `south`, `west`, `up` or `down` turns, an `axis` of `x`, `y` or `z` swaps `x` and `z` on a
quarter turn, a `rotation` from 0 to 15 turns like a standing sign's, and `north`, `east`, `south` and `west`
properties (a fence's connections) trade values. Other properties are kept, and a block whose turned state it doesn't
have (a `facing` without `east`, say) stays as it is.

### Block hooks

```csharp
public sealed class PipeBehavior : IBlockBehavior
{
    // The block's type, from RegisterBlock.
    public BlockType Pipe { get; set; } = null!;

    public IBlock? GetStateForPlacement(BlockPlaceContext context) =>
        this.Pipe.With(context.Block, "facing", context.NearestLookDirection.Opposite().ToString().ToLowerInvariant());

    public void OnPlaced(BlockPlacedContext context) =>
        context.Level.ScheduleTick(context.Position, context.State, delay: 4);

    public void NeighborChanged(NeighborChangedContext context)
    {
        // It breaks when the block below it goes.
        if (context.Level.GetBlock(context.Position + Vector.Down).IsAir)
            context.Level.RemoveBlock(context.Position);
    }

    public void Tick(BlockContext context) => context.Level.ScheduleTick(context.Position, context.State, delay: 4);
}
```

A block's `Behavior` gets vanilla's block hooks; each defaults to doing nothing:

- `UseAsync`: a player uses the block (right-clicks it without sneaking), before the item in their hand.
- `GetStateForPlacement`: the state a player places the block in, from a `BlockPlaceContext`: the player, hand and
  item, the clicked block, face and where on it they clicked (`ClickedAt`, 0 to 1 on each axis), where the block goes
  and whether it replaces the clicked block (tall grass, say), and the player's `HorizontalDirection` and
  `NearestLookDirection`, worked out like vanilla's. Null, or an exception logged by the host, rejects placement:
  the player keeps the item and receives the block, inventory and prediction-acknowledgement corrections.
- `OnPlaced`: the block was placed, or changed state (`OldState` says which), like vanilla's `onPlace`.
- `OnRemoved`: another block replaced it with neighbour updates, like vanilla's `affectNeighborsAfterRemoval`.
- `NeighborChanged`: a neighbour changed with neighbour updates (`SourcePosition`, and `SourceBlock`, the state it
  replaced), like vanilla's `neighborChanged`.
- `UpdateShape`: the block's state after the neighbour in `Direction` became `NeighborState`, like vanilla's
  `updateShape`: a fence's connections, or air for a block that lost its support, which destroys it (leaving the
  fluid it held, without drops).
- `Tick`: a tick scheduled for the block came due, like vanilla's `tick`.
- `RandomTick`: a random tick, like vanilla's `randomTick`, for blocks registered with `RandomlyTicks = true`. Random
  ticks reach sections near players like vanilla's (see [Saplings](#saplings)): at the default speed, each block
  about once every 68 seconds.
- `EntityInsideAsync` and `SteppedOnAsync`: each tick an entity's box overlaps the block, or the entity stands on it.

The hooks with a `Level` run synchronously while the level's block updates wait for them, like vanilla's: read and
change blocks through that `IBlockLevel` (`GetBlock`, `SetBlock`, `RemoveBlock`, `ScheduleTick`,
`HasScheduledTick`, `UpdateNeighbors`, its [redstone](#redstone) signals and the level's `Random`), not through
`ILevel`'s asynchronous methods, and keep them short. Neighbour and shape updates triggered inside another neighbour
or shape callback wait until that callback returns, in vanilla's order. Placement and removal callbacks
run synchronously during the block change, like vanilla's `onPlace`; a change inside them can run another placement
callback or start neighbour updates before the outer callback returns. Keep placement chains bounded (see
[known gaps](known-gaps.md#recursive-placement-callbacks)). `IBlockLevel.GetBlockEntity` reads the block's block
entity, like vanilla's `getBlockEntity`; a hook may run on a player's thread while block entities tick, so read only
what's safe to read so (a field written whole) and change the block entity on its own tick. A hook that throws is
logged and the updates, and the level's tick, go on. Like `UseAsync`, every hook runs as a call into the plugin, and
none runs once it starts unloading. A block change and the updates it causes run as one step: a change made
meanwhile on another thread waits for them.

Block changes run vanilla's side effects as `BlockUpdateFlags` ask, with vanilla's values: `ILevel.SetBlockAsync`
with `doBlockUpdate: true`, a player placing or breaking a block, and explosions use `All` (neighbours hear of the
change, then get shape updates); `SetBlockAsync(position, block, flags)` and `IBlockLevel.SetBlock` take any.
Without `Clients`, players aren't sent the change. Without `Neighbors`, the old block isn't told it was removed, the
neighbours don't get `NeighborChanged` and nether portals don't check their frames; `KnownShape` skips shape updates,
and `SkipOnPlace` skips `OnPlaced`. `doBlockUpdate: false` is a plain change, without hooks or updates. Of vanilla's
blocks, liquids react to the updates (they flow), falling blocks fall two ticks after they're placed or a neighbour
changes, [redstone components](#redstone) react like vanilla's, and the other vanilla blocks only change shape when
water or lava changes blocks (see [known gaps](known-gaps.md)).

`ILevel.ScheduleBlockTick` and `IBlockLevel.ScheduleTick` schedule a block's tick in a number of game ticks, with a
`TickPriority`, like vanilla's `scheduleTick`: one per block and position, run if the block is still there then.
Ticks due the same game tick run by priority, then in the order they were scheduled, before fluid ticks and
entities. They're saved with their chunk as vanilla's `block_ticks`. A plugin's block's ticks wait while the plugin
unloads or is unloaded, keeping their priority and place in the order, and run once it's back.

A cancellable `BlockPlaceEventArgs`, next to `BlockBreakEventArgs`, is raised before a player places a block, with
the state they'd place. Cancelling it keeps the block from being placed, and the player keeps the item. So does a
handler that fills the target or changes the player's hand: both are checked again as the block is placed, and the
item is only used once it is.

### Runtime light

Placed and removed blocks update block light and sky light, including a plugin block's `LightEmission` and its
template's opacity and light-occluding shapes. Player and plugin changes, fluids, explosions and growing trees share
the same runtime engine. Changes are batched on the level's tick; light is resolved before random ticks, so saplings
read the current light, and before a chunk is saved. Players watching a changed chunk receive one light packet per
tick containing the changed sections, including sections that became dark. Dimensions without `HasSkylight` do not
propagate sky light.

Direct `IChunk.SetBlock` and `IChunkSection.SetBlock` calls on loaded, complete chunks use the same lock and relight
hook, with section coordinates local to its 16-block cube. Unfinished generation writes keep their direct storage
path. Loading, generation completion and unloading attach or detach lighting with the chunk slot's lifecycle;
reading a loaded chunk does not acquire the tick lock.

Initial chunk packets capture immutable light masks and arrays under the propagation gate before entering the send
queue, like runtime light updates. Serialization does not take the tick lock. A registered block's `LightEmission`,
effective opacity and collision template cannot change on hot reload until the server restarts: registration fails
with a logged restart-required message and retains the old settings. This keeps loaded and saved illumination and
sky heights consistent without a world-wide relight.

Light stops at unloaded or unfinished chunks. When a complete neighbor loads, both sides of its shared borders are
reconciled on the next tick: only border blocks whose light disagrees with their neighbors are relit. A chunk that
generation completes skips this only if its current instance ran lighting under overlapping-area locks and no runtime
relight reached it while it was unfinished. Saved prototypes that resume after lighting still reconcile. Mojang
generation lighting runs in parallel under its chunk locks; generators without those locks serialize lighting under
the tick lock and reconcile at completion. Unlike vanilla's chunk light storage, Obsidian stores arrays inside the
build range; the top sky source is virtual, and the extra sections outside the build range are not sent. Lighting does
not load or generate missing neighbors to finish an update.

Runtime updates clear dependent light and spread surviving sources again; they do not recompute complete chunks
for each changed block. A deterministic test times 288 edits batched over nine 64-layer chunks against a fresh
generation-engine computation after each batch. Timing is reported by `BatchedEditsMatchFullComputationAndReportTiming`
in `Obsidian.Tests/RuntimeLight.cs`; large skylight changes can still affect an entire column and its surroundings.

### Redstone

```csharp
public sealed class SensorBehavior : IBlockBehavior
{
    // The block's type, from RegisterBlock, with a "powered" property.
    public BlockType Sensor { get; set; } = null!;

    // It powers every side while powered, like a lever; dust connects to it.
    public bool IsSignalSource(IBlock state) => true;

    public int GetSignal(SignalContext context) => this.Sensor.GetValue(context.State, "powered") == "true" ? 15 : 0;

    // Comparators facing away from it read 1 to 15.
    public bool HasAnalogOutput(IBlock state) => true;

    public int GetAnalogOutput(SignalContext context) => 8;

    // It turns on and off with the power it gets, like a lamp, and tells its neighbours.
    public void NeighborChanged(NeighborChangedContext context)
    {
        var powered = context.Level.HasNeighborSignal(context.Position);
        var state = this.Sensor.With(context.State, "powered", powered ? "true" : "false");
        context.Level.SetBlock(context.Position, state);
    }
}
```

Redstone power goes from 0 to 15, like vanilla's. A block gives each neighbour a weak signal, which powers that
neighbour (a lever powers the dust beside it), and a strong one, which also powers through the neighbour when it's a
conductor (a lever powers the block it's on, and that block powers the dust beside it). Conductors are what vanilla
calls redstone conductors, full solid blocks like stone; a plugin block is one when its template is (opaque blocks
with collision).

`ISignalReader` reads power like vanilla's `SignalGetter`, on the `IBlockLevel` a hook gets and on `ILevel`:
`GetSignal` (what the block at a position gives the block asking, or for a conductor the most strong power it gets),
`GetDirectSignal` (its strong power), `GetDirectSignalTo`, `HasSignal`, `HasNeighborSignal` (whether anything powers
a position, what a lamp lights by) and `GetBestNeighborSignal`. Directions are vanilla's: the direction from the block
asking to the block at the position, so a block reads its west neighbour with
`GetSignal(position + Vector.West, BlockFace.West)`. `ILevel`'s methods wait for the block updates running at the
time, like its block changes.

A plugin block takes part through its behaviour's hooks, which default to giving no power:

- `IsSignalSource`: whether it gives power, like vanilla's `isSignalSource`. Dust connects to it from every side, and
  comparators take its strong power from their sides.
- `GetSignal` and `GetDirectSignal`: its weak and strong power toward the neighbour asking (`SignalContext.Direction`
  points from that neighbour to it). They run whenever a neighbour reads its power, so keep them a quick read of the
  state. When the power changes, tell the neighbours with `IBlockLevel.UpdateNeighbors` (a block change with
  neighbour updates does), and for strong power the neighbours of the block it powers through too, as vanilla's
  levers do.
- `HasAnalogOutput` and `GetAnalogOutput`: what a comparator facing away from it reads, like a container's fullness.
  A block change with neighbour updates tells the comparators, and so does its block entity's `SetChanged` at the end
  of the tick, like vanilla's `setChanged`, so an output read from the block entity (`IBlockLevel.GetBlockEntity`)
  follows it; `IBlockLevel.UpdateAnalogOutput` tells them when the output changes otherwise.

It reacts to power in `NeighborChanged`, which runs when power around it may have changed, as vanilla's blocks do.
The hooks run in a call into the plugin like the others; while the plugin unloads or is unloaded, its blocks give no
power and have no analog output. As it unloads, and when it loads again, the blocks around its blocks placed in loaded
chunks hear of them like of a block change (neighbour and shape updates, and comparators), so that the circuits they
power follow. When a chunk becomes available, its placed plugin blocks and plugin sources across its border send
the same updates, so circuits also catch up with changes made while absent. Palette checks skip vanilla-only
sections, and worlds with no registered plugin blocks skip reconciliation entirely.

Vanilla's components work at runtime through the same updates and ticks, in vanilla's order and with vanilla's
delays and tick priorities: redstone dust (vanilla's default wire evaluator, its connections, and dots and crosses
players switch between), redstone torches (two game ticks, burning out after 8 toggles within 60 game ticks for 160),
repeaters (delays of 1 to 4 redstone ticks, and locking), comparators (comparing and subtracting, reading stored
container contents without rolling pending loot, cakes, composters, cauldrons, beehives, end portal frames,
respawn anchors and item frames, behind a conductor too),
levers, buttons (pressed by players and wind charges, and wooden ones by arrows), pressure plates (entities on them;
wooden ones feel any entity, stone ones mobs and players, weighted ones count them), the block of redstone and the
redstone lamp. Players place them as vanilla does: facing and attached from where they click and look, where they can
stay. Those that lose their support pop off, dropping their loot. A comparator's output is saved in its block
entity, and vanilla saves' scheduled ticks of these blocks run. Other powered blocks (doors, pistons and the like)
don't react yet (see [known gaps](known-gaps.md#redstone)).

### Saplings

```csharp
registry.RegisterBlock("ruby:ruby_sapling", new BlockProperties
{
    StateProperties = [StateProperty.Int("stage", 0, 1)],
    HasCollision = false,
    Opaque = false,
    Hardness = 0,
    TreeGrower = new TreeGrower
    {
        Tree = "ruby:ruby_tree",
        MegaTree = "ruby:huge_ruby_tree",
        SecondaryTree = "minecraft:fancy_oak",
        SecondaryChance = 0.1f
    }
});
```

A block with a `TreeGrower` is a sapling, like vanilla's: on random ticks with a light level of 9 or more above it, one
time in 7, and with bone meal 45% of the time, it grows a step. With a `stage` property the first step sets it to 1
and the second grows the tree; without one, every step grows it. Its trees are configured features named by
identifier: vanilla's, or a plugin's or data pack's from `worldgen/configured_feature` files (see
[World generation features](#world-generation-features)), looked up as the sapling grows, so a reload or override
applies at once. Like vanilla's, four saplings of the block in a square grow its `MegaTree` from their north-west
corner, a flower within two blocks makes it grow its `Flowers` tree, and `SecondaryChance` picks the secondary tree
of either; a sapling whose feature is missing, or can't be placed, stays a sapling. Like vanilla's, random ticks only
reach chunks near players whose neighbours are loaded, and a tree that would reach a chunk that isn't loaded doesn't
grow. The grower belongs to the plugin like the block's behaviour: its saplings stop growing when it unloads, and a
reload replaces it.

Vanilla's saplings, azaleas and the mangrove propagule grow the same way into vanilla's trees, and bone meal grows
crops, grass blocks (with vanilla's `minecraft:grass_bonemeal` and the biome's flowers) and short grass and ferns.
Bone meal is used on the level's next tick, on the block there then: it's used up (outside creative mode) only if
that block still takes it.

## Blocks from JSON

Each `data/<namespace>/blocks/<name>.json` in the plugin's data folder is a block, shaped like an entry of vanilla's
blocks report:

```json
{
  "properties": {
    "lit": ["true", "false"],
    "power": ["0", "1", "2", "3"],
    "color": ["red", "green"]
  },
  "default": { "lit": "false" },
  "hardness": 0.5,
  "lightEmission": 7,
  "behavior": "Ruby.LampBehavior",
  "item": { "maxStackSize": 16 }
}
```

True and false make a bool property and consecutive numbers an int one; anything else is an enum property in the
order listed. The other keys are `BlockProperties`' (`hasCollision`, `opaque`, `hardness`, `explosionResistance`,
`requiresCorrectTool`, `lightEmission`, `lootTable`), `behavior` is an `IBlockBehavior` type's full name, and
`treeGrower` makes a sapling (`{"tree": "ruby:ruby_tree"}`, with its other members in camel case). `item` is false for a
block without an item.

The generator makes a `RubyBlocks` class (from the namespace) in the plugin assembly's namespace, with a property per
block, a typed state per block with properties (`RubyBlocks.LampState`, with `From(IBlock)` and `ToBlock()`) and enums
for enum properties. Register them from `ConfigureRegistry`:

```csharp
RubyBlocks.Register(registry);
var state = new RubyBlocks.LampState(Lit: true, Power: 2, Color: RubyBlocks.LampColor.Green).ToBlock();
```

Obsidian.MSBuild gives the data folder's files to the generator; without it, add the project's `data` folder's files
as `AdditionalFiles`.

## Block drops

Blocks use vanilla loot tables when players break them and when explosions destroy them. Vanilla's blocks use their
declared table, including aliases such as wall torches using the torch table. A plugin block `ruby:ore` uses
`ruby:blocks/ore`, loaded from `data/ruby/loot_table/blocks/ore.json`:

```json
{
  "type": "minecraft:block",
  "pools": [
    {
      "rolls": 1,
      "entries": [
        {
          "type": "minecraft:item",
          "name": "ruby:gem",
          "functions": [
            { "function": "minecraft:set_count", "count": 2 },
            { "function": "minecraft:explosion_decay" }
          ]
        }
      ]
    }
  ]
}
```

`BlockProperties.LootTable` names another table, such as `ruby:rewards/ore`; JSON block definitions use `lootTable`.
Without an explicit table, a plugin block whose default table is absent drops its own item. An explicit table that
is missing, or a table with no pools, drops nothing. Creative and spectator players receive no drops.

Tables can add or replace any namespace's tables. Files are applied in plugin load order, then the server's data
packs in their name order. Reload replaces the plugin's tables; unloading removes them and restores the previous
table or vanilla's. Nested `minecraft:loot_table` entries resolve through the same overrides. A malformed table or
unsupported loot type is logged and omitted, without preventing the plugin from loading.
Each block evaluation captures one table version for its random sequence, location checks and nested references;
an in-progress evaluation finishes against that version if a plugin reloads. Table entries require `value` to name
a table or contain an inline table. An evaluation failure during an explosion is logged for that block, and the
explosion continues removing blocks and evaluating their drops.

The evaluator supports item, empty, dynamic and table entries; alternatives, groups and sequences; constant,
uniform and binomial number providers; and the block conditions `match_tool`, `block_state_property`, `table_bonus`,
`random_chance`, `survives_explosion`, `all_of`, `any_of` and `inverted`. Tool predicates accept item ids or item tags,
counts and active enchantments, including silk touch and fortune. `location_check` supports block ids or tags and
state properties at an offset; vanilla's empty `entity_properties` predicate requires the breaking entity.

Functions include `set_count`, `limit_count`, all three vanilla `apply_bonus` formulas, `explosion_decay`,
`copy_state` and `copy_components`. Explosion decay samples each item with probability `1 / radius`; survival
conditions sample once. The explosion's drop-decay game rule decides whether a radius is supplied. Conditions and
functions run in table order, with vanilla's short circuiting and random draws. Named random sequences are kept
per level in memory, shared with container loot; persisting them remains a separate save-format task.
Evaluations sharing a named sequence serialize their complete draws; unrelated sequences remain independent.

Breaking either part of a vanilla door, bed or double plant removes its matching partner and evaluates the
drop-bearing state once, before removal. Creative breaks suppress both parts' drops. Infested blocks keep table
results, including plugin overrides; silverfish spawning without silk touch is handled separately.

`RequiresCorrectTool` applies to vanilla and plugin blocks when players break them. A tool's `minecraft:tool`
component uses the first matching `correct_for_drops` rule. Vanilla's defaults read the live `mineable/*` and
`incorrect_for_*_tool` block tags, including tier restrictions. A plugin can add its blocks to those tags or supply
rules on a tool stack. Removing the tool component makes the stack unsuitable for required-tool blocks. Explosions
evaluate tables with an empty tool and bypass the player's correct-tool check.
Their direct source entity, when present, supplies `this_entity`, independently of its game mode.

For direct evaluation, `LootTableJson.Parse(string json, string identifier = "")` builds a `LootTable`.
`LootContext` adds `Tool`, `BlockState`, `BlockProperties`, `ExplosionRadius`, `BlockEntityComponents`,
`DynamicDrops` and `TestLocation`. `copy_components` respects `include` and `exclude`, and `copy_state` copies
requested properties onto the stack's `minecraft:block_state` component.

Players' breaks and explosions take the broken block entity's components and dynamic drops before the block goes,
like vanilla's `BlockEntity.collectComponents` and blocks' `getDrops`: a container's `custom_name`, `lock` and
`container_loot` (the loot table it's still waiting for), a shulker box's `container` and its `minecraft:contents`
dynamic drop, a decorated pot's `pot_decorations` and its `minecraft:sherds` dynamic drop, and other named block
entities' `custom_name`. So a broken shulker box drops a box holding its items (rolling the loot it was waiting for
first when a player breaks it, and dropping even for a creative player when it holds items), and placing that box
gives them back, with its name, lock and loot table. Chests and other containers whose items drop as they go give
no `container`, like vanilla's. Item stacks save `container`, `lock`, `container_loot` and `pot_decorations`.
Placement initializes the block entity from the held item's supported components before publishing its block,
then consumes the same hand item inside the block mutation lock. Decorated pots apply `pot_decorations` and their
first `container` item and queue their initial client data. Removing a decorated pot spills that item once, separately
from the pot or sherd drops, like vanilla; its loot table copies only decorations. A failed shulker loot evaluation
restores retirement while the box is still standing; malformed `limit_count` bounds are rejected when the table is
loaded.
Banner patterns, bees and skull profiles aren't taken yet. A plugin's block entity adds what its
`CollectComponents` sets, in the plugin's [component types](#item-components), and reads it back in
`ApplyComponents` (see [Block entities](#block-entities)); a plugin block without a loot table drops its item with
those values.

Player breaks of plugin blocks and explosions needing their live block entities' components queue the whole break
to the level's next tick, including `CollectComponents`, loot evaluation and removal. Explosions still apply entity
damage immediately; blocks kept as data after plugin unloading break immediately too. A queued explosion break
checks the block state and block entity again, so replacing the target before that tick cancels its break. Only
serial tick operations explicitly collect inline; background tasks wait for the level's simulation gate, including
tasks started during a tick.

## Items

```csharp
var pickaxe = registry.RegisterItem("ruby:ruby_pickaxe", new ItemProperties
{
    MaxStackSize = 1,
    MaxDamage = 900,
    Enchantable = 12,
    Tool = ComponentBuilder.Tool with
    {
        Rules =
        [
            new ToolRule
            {
                Blocks = new IdSet { Type = 0, TagName = "minecraft:incorrect_for_iron_tool" },
                CorrectDropForBlocks = false
            },
            new ToolRule
            {
                Blocks = new IdSet { Type = 0, TagName = "minecraft:mineable/pickaxe" },
                Speed = 7,
                CorrectDropForBlocks = true
            }
        ]
    },
    AttributeModifiers =
    [
        new AttributeModifier
        {
            Id = AttributeIds.AttackDamage,
            Name = "minecraft:base_attack_damage",
            Value = 4,
            Operation = AttributeOperation.Add,
            Slot = AttributeSlot.MainHand
        }
    ],
    Behavior = new RubyPickaxeBehavior()
});

registry.RegisterItem("ruby:ruby_apple", new ItemProperties
{
    Food = ComponentBuilder.Food with { Nutrition = 4, SaturationModifier = 0.3f }
});

registry.RegisterItem("ruby:ruby_helmet", new ItemProperties
{
    MaxStackSize = 1,
    MaxDamage = 300,
    Equippable = ComponentBuilder.Equippable with { Slot = EquipmentSlot.Helmet },
    AttributeModifiers =
    [
        new AttributeModifier
        {
            Id = AttributeIds.Armor,
            Name = "minecraft:armor.helmet",
            Value = 3,
            Operation = AttributeOperation.Add,
            Slot = AttributeSlot.Head
        }
    ]
});
```

A plugin's item is in its namespace and keeps its id across reloads; unloading the plugin drops its behaviour but
not the item, its properties or its stacks.

**Default components.** `MaxStackSize`, `MaxDamage` and `Enchantable`, and `Tool`, `Food`, `Consumable`,
`Equippable` and `AttributeModifiers` built from `ComponentBuilder` with a `with` expression, are the item's default
components, like a vanilla item's: every stack has them unless it sets or removes its own, and they aren't sent or
saved with stacks, since the client mod registers the same item with the same properties (`Item.DefaultComponents`
holds the component ones). Obsidian honours them as it does vanilla's:

- `Tool`: its rules, the first matching one deciding, give the mining speed and whether the item is the correct
  tool for a block's drops (by block tag, `Type = 0` and `TagName`, or by block ids); breaking a block that doesn't
  break instantly, outside creative, wears the item by its damage per block. Mining speed is the client's: the
  server doesn't time breaking, for any item.
- `Food`: a player eats the item by using it (at any hunger with `CanAlwaysEat`), gaining its nutrition and
  saturation. `Consumable` sets how it's eaten and the effects it gives; it defaults to vanilla's food
  (`ComponentBuilder.Consumable`, 1.6 seconds) and does nothing without `Food`.
  Default effects must be the host's sealed `ComponentConsumeEffect` or `EffectWithProbability` values; registration
  rejects other `IConsumeEffect` implementations, so retained defaults cannot keep an unloaded plugin's assembly
  alive.
- `Equippable`: a player wears the item in its slot by using it, or by placing it in the armour slot.
  `ComponentBuilder.Equippable` has vanilla's defaults; set the slot.
- `AttributeModifiers`: what the item adds while it's in the modifier's slot, by attribute id (`AttributeIds`).
  A player gets them while holding or wearing the item, for every attribute players have (see
  [Attributes](#attributes)); mobs apply only their equipment's armour, toughness and knockback resistance.

Changing an item's defaults in a reload reaches stacks made, loaded or received afterwards; stacks already in memory
keep the defaults they were made with until then.

**Item hooks.** An item's `Behavior` (an `IItemBehavior`) gets these hooks, each doing nothing by default:

- `UseAsync`: a player uses the item without aiming at a block (right-clicks the air), before vanilla's handling
  (eating, equipping). Returning true handles the use.
- `UseOnBlockAsync`: a player uses the item on a block, after the block's own use and before the item places a block.
- `UseOnEntityAsync`: a player uses the item on a living entity (an `ItemUseOnEntityContext` with the `Entity`), on
  the entity's level tick. Like vanilla's `Player.interactOn`, it runs after the entity's own interaction (a
  villager's trading, feeding an animal, a plugin entity type's `IEntityBehavior.InteractAsync`) and only when that
  didn't take it. Mobs that can be ridden (tamed horses, camels, adult happy ghasts and nautiluses) give the item its
  use before the player mounts, as vanilla's do, so there it may run twice in one use when it returns false. In
  creative mode the context's `Stack` is a copy, so that the use leaves the player's stack as it was.
- `InventoryTickAsync`: each server tick, on the player's tick, for every stack of the item in a player's inventory
  (the hotbar and main inventory, then the off hand and armour), like vanilla's `inventoryTick`. The
  `ItemInventoryTickContext` gives the stack, its `Slot` (5 to 8 armour, 9 to 35 the main inventory, 36 to 44 the
  hotbar, 45 the off hand) and its `EquipmentSlot`: `MainHand` for the selected hotbar slot (`IsSelected`), the off
  hand or an armour slot, null elsewhere. It runs holding the inventory's lock, like a click, so it never sees a
  click half done: change the stack or the inventory through their own members, and the slots it changed are sent
  to the player after the tick; don't wait on anything that takes the lock. Players' inventories are only scanned
  while some item's behaviour implements this hook, so leave it out when the item doesn't need it.

The hooks are calls into the plugin: none starts once the plugin begins unloading, and one that throws during the
inventory tick is logged without stopping the tick.

### Item components

```csharp
var charge = registry.RegisterComponentType(
    "ruby:charge",
    new ComponentCodec<int>(value => new NbtTag<int>("", value), tag => ((NbtTag<int>)tag).Value),
    new ComponentNetworkCodec<int>((writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt()));

stack.Set(charge, stack.Get(charge) + 1);
if (stack.TryGet(charge, out var charges)) { /* ... */ }
stack.Remove(charge);
```

A data component type, like a NeoForge mod's `DataComponentType`, holds a plugin's own data on stacks of any item.
`IPluginRegistry.RegisterComponentType<TValue>(identifier, codec, networkCodec)` returns a
`PluginComponentType<TValue>`, which `ItemStack.TryGet`, `Get`, `Set` and `Remove` take. The identifier is in the
plugin's namespace, and the type's id (`Type`, a `DataComponentType` value) follows vanilla's last one, in
registration order, and stays for the process: registering it again, as a reload does, gives the same id with the new
codecs. Values are immutable on the stack: `Set` a new one to change it. Like vanilla's component types, a plugin's
type is never an item's default, so removing it just takes it off the stack.

- **Saving.** The `ComponentCodec` saves a value as NBT, in the stack's `components` under the type's identifier
  beside vanilla's (`{ id, count, components: { "minecraft:damage": 3, "ruby:charge": 2 } }`), and reads it back.
  The stack keeps only that saved form, so it holds nothing of the plugin's: `Set` encodes and `Get` decodes.
- **Clients.** With a `ComponentNetworkCodec`, the type is in the `minecraft:data_component_type` registry synced to
  NeoForge, Fabric and Obsidian clients, which must register the same type (the Obsidian client registers its mods'
  with the same `ConfigureRegistry` code), and its values are sent with stacks. Like a plugin's items, a synced type
  keeps vanilla clients out and stays synced until the server stops. Without a network codec, the type is the
  server's own, like vanilla's components that aren't synced: it isn't in the synced registry and its values never
  leave the server, so it doesn't keep vanilla clients out. Vanilla block entity updates filter their item NBT through
  the same visible patch; disk saves keep all components. Click predictions default to hashing the saved NBT. When NBT
  loses the persistent codec's type distinctions, set `ComponentNetworkCodec<TValue>.Hash` (`Func<TValue, int>?`) to
  the client's `HashOps` value using `ComponentHash`, for example `Hash = ComponentHash.Boolean` for a boolean codec.
  The callback receives the decoded value and runs under the same plugin call guard as the codecs.
- **Unloading.** When the plugin unloads, its types lose their codecs but keep their ids, and stacks keep their
  values as saved, saving and loading them unchanged, and comparing them, so they still stack. They aren't sent to
  clients meanwhile. Codec calls admitted before unloading finish before release; new calls are refused. `TryGet`
  returns false (and `Get` returns default) through an unloaded plugin's type handle; `Set` throws
  `InvalidOperationException`. Use the new handle after reload. Once the plugin is back, its codecs read the values
  again. A value saved before its type's first registration is projected as a modeled component for networking,
  equality and hashing once registered, without mutating the saved stack.
- **Block entities.** A plugin's block entity keeps its data on its block's item in the plugin's own types through
  `CollectComponents` and `ApplyComponents` (see [Block entities](#block-entities)).

### Creative tabs

```csharp
registry.RegisterCreativeTab("ruby:rubies", new CreativeTabProperties
{
    Title = new ChatMessage { Translate = "itemGroup.ruby.rubies" },
    Icon = pickaxe,
    After = "minecraft:spawn_eggs",
    Items = [pickaxe],
    DisplayItems = () => RubyVariants.All.Select(variant => variant.Item)
});

registry.AddToCreativeTab("minecraft:tools_and_utilities", [pickaxe], after: ItemsRegistry.NetheritePickaxe);
```

Like a NeoForge mod's creative tabs, a plugin's are registered in `ConfigureRegistry` on both sides, and shown by the
Obsidian client:

- **Tabs.** `RegisterCreativeTab(identifier, properties)` adds a tab in the plugin's namespace. Its `Title` is a
  translation key (from the mod's language files) or plain text (`ChatMessage.Simple`), its `Icon` an item. It shows
  `Items`, then whatever `DisplayItems` gives each time the creative inventory opens, like NeoForge's display items
  generator; the generator runs inside a call into the plugin, and one that throws is logged and leaves its items
  out. Tabs without items aren't shown. Tabs go after vanilla's, in registration (load) order, or right after the
  tab named by `After` (vanilla's or another plugin's, like NeoForge's `withTabsBefore`).
- **Adding to tabs.** `AddToCreativeTab(tab, items, after)` adds items, the plugin's or vanilla's, to any tab by
  identifier, like NeoForge's `BuildCreativeModeTabContentsEvent`: at the end, or right after the last of `after`'s
  stacks when the tab shows it. Additions to the hotbar, search and inventory tabs, or to a tab that isn't
  registered, aren't shown.
- **Pages and search.** Like NeoForge's, tabs are paged: ten tabs with items to a page, five on top and five below,
  with the hotbar, search and inventory tabs on every page, so vanilla's fill the first page and plugins' follow on
  the next ones, turned with the buttons above the tabs. The search tab shows every tab's items in tab order, like
  vanilla's, so plugins' items are found too. A plugin's item in no tab isn't shown anywhere, as on NeoForge; put
  it in a tab.
- **Servers.** Like vanilla's and NeoForge's, creative tabs are the client's: the `minecraft:creative_mode_tab`
  registry isn't synced and nothing on the wire names a tab, so a server only checks the registrations (the same
  checks as the client's) and keeps nothing. A creative player sets stacks with the creative slot packet, which
  accepts any item, a plugin's included, with its default components; vanilla clients can't be sent one, since
  they're refused once a plugin registered items.
- **Unloading.** A plugin's tabs and additions are removed when it unloads and registered again on reload, on both
  sides; the client builds the tabs again each time the creative inventory opens.

## Progress

Connected players expose `IPlayer.Statistics` and `IPlayer.Advancements`. Both persist in the owning world's
vanilla files: `stats/<uuid>.json` has `stats` grouped by registry type, and `advancements/<uuid>.json` has criterion
completion dates and `done`, with `DataVersion` in both. Dimension travel keeps the same world's progress.

```csharp
player.Statistics.Increment("minecraft:jump");
player.Statistics.Increment("minecraft:diamond", 3, "minecraft:picked_up");
var jumps = player.Statistics.Get("minecraft:jump");

await player.Advancements.GrantAsync("ruby:powered", "first_machine");
var powered = player.Advancements.IsDone("ruby:powered");
var obtained = player.Advancements.HasCriterion("ruby:powered", "first_machine");
player.Advancements.Revoke("ruby:powered", "first_machine");
```

`Get(identifier, type = "minecraft:custom")` returns zero when there is no value. `Increment(identifier,
amount = 1, type = "minecraft:custom")` accepts registered identifiers and a nonnegative amount, saturating at
`int.MaxValue`. The types are vanilla's `mined`, `crafted`, `used`, `broken`, `picked_up`, `dropped`, `killed`,
`killed_by` and `custom`, all in the `minecraft` namespace. Play time and time since death/rest are ticks;
movement is centimetres; damage uses tenths of a health point. The server records these at existing movement,
combat, inventory, crafting, consumption and item-use hooks and answers the client's statistics request.

Register a plugin custom statistic during `ConfigureRegistry` with `RegisterCustomStatistic("ruby:machines")`,
which returns its protocol id. It joins `minecraft:custom_stat` in NeoForge/Fabric registry sync and the content
version checked on reload; clients must provide the same registry entry. Identities stay for the process after
unload, while incrementing an unloaded statistic is refused. Saved values and unknown stat identifiers survive.
This server API does not add custom-stat registration to the Obsidian client's plugin registry yet.

Advancements are vanilla JSON in `data/<ns>/advancement/<path>.json`, loaded from the generated vanilla assets,
then plugins, then data packs. The engine sample's `sample:grinder` advancement demonstrates `inventory_changed`
and an experience reward. Criteria have a trigger and detached conditions. Requirements are an AND of rows, each
an OR of criterion names; omitting requirements makes each criterion its own required row. Parent, display,
rewards and `sends_telemetry_event` are read. Join sends a reset and every definition/progress entry, and tick-end
updates carry changed progress. Content reload resets the tree. The client can select an advancement tab.

`GrantAsync(identifier, criterion = null)` grants one criterion or the whole advancement and returns whether
anything changed. Completing the requirements grants experience, generates reward loot into inventory (overflow
drops), and records/adds reward recipes to the existing recipe book. Repeating a grant is inert; revoking and
completing again grants rewards again, as in vanilla. `Revoke(identifier, criterion = null)` returns whether
anything changed. Granting/revoking missing advancement or criterion ids returns false. Saved plugin progress stays
when its content unloads and resumes when it returns.
`/advancement grant|revoke <player> only <id> [criterion]` uses the same API
and the `advancement` permission.

### Plugin triggers

```csharp
// During ConfigureRegistry, on the server:
var powered = registry.RegisterAdvancementTrigger("ruby:powered",
    new AdvancementConditionsCodec<int>(json => json.GetProperty("minimum").GetInt32()));

// When a machine starts, evaluate each matching criterion's decoded conditions:
await powered.FireAsync(player, minimum => machine.Power >= minimum);
```

The JSON criterion names `"trigger": "ruby:powered"` and `"conditions": { "minimum": 10 }`. The generic
`RegisterAdvancementTrigger<TInstance>(identifier, AdvancementConditionsCodec<TInstance> conditions)` returns an
`AdvancementTrigger<TInstance>` whose `FireAsync(IPlayer, Func<TInstance, bool>)` checks that player's criteria.
Conditions decode only while firing inside the plugin's active call. Throw `NotSupportedException` or a JSON
validation exception for unsupported conditions. Old handles become inert on unload and cannot fire a replacement
registration. Player progress never retains plugin condition instances or codecs.

The current vanilla triggers are `inventory_changed`, `recipe_unlocked`, `player_killed_entity`,
`entity_killed_player`, `changed_dimension`, `location` (every 20 ticks), `consume_item`, `placed_block`,
`item_used_on_block`, `tick`, `recipe_crafted`, `enchanted_item`, and the transition into `Sleeping` for
`slept_in_bed`. `impossible` only advances through explicit grants. Vanilla condition matching supports inventory
slot counts; loot's item/id/tag/count/enchantment predicates; entity type; dimension and coordinate ranges; block
id/tag/state predicates; and supported `entity_properties`, `location_check`, `block_state_property` and
`match_tool` checks. Unsupported triggers and condition fields never match.

Full vanilla visibility, predicates, selective recipe unlocking, breeding attribution, function rewards and
remaining event coverage are recorded in [known-gaps.md](known-gaps.md#progress-parity). Progress is usable but the
engine plan's Progress item is not marked done.

## Travel and portals

Move a player or any server entity without replacing its state:

```csharp
await entity.TeleportAsync(targetLevel, new VectorD(8.5, 72, -3.5), yaw: 90f, pitch: 0f);
```

The API is `IEntity.TeleportAsync(ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null)`.
Omitted rotations retain their values. Destination chunks are loaded before the entity leaves; a player's
level change uses the existing respawn path, keeping attributes, metadata, health, food, inventory, effects,
attachments and velocity. The shared world's spawn, target level's difficulty, time and weather, and the player's
abilities and experience are sent again. The `IWorld` and `IEntity` overloads use the same path. Coordinates must be
finite and
inside the server's global coordinate limits; they may be above or below the build range.

Supported mounts carry their player passengers, retain the passengers' offsets and remount them after travel.
A player teleported independently dismounts. Obsidian does not yet model general entity passenger trees or leashes;
see [known-gaps.md](known-gaps.md#travel-passengers-and-leashes).

Register rectangular portals on the server in `ConfigureRegistry`. A dedicated block makes its placed portals
unambiguous and keeps them inert when the plugin unloads:

```csharp
var frame = registry.RegisterBlock("ruby:portal_frame");
var gateway = registry.RegisterBlock("ruby:crystal_portal", new BlockProperties
{
    StateProperties = [StateProperty.Enum("axis", "x", "z")],
    HasCollision = false,
    Opaque = false
});

var portal = registry.RegisterPortal("ruby:crystal_gate", new PortalProperties
{
    IsFrame = frame.Is,
    FrameBlock = frame.DefaultState,
    PortalBlock = gateway.DefaultState,
    Igniter = ItemsRegistry.Diamond,
    WaitTicks = 40,
    ResolveTarget = level =>
    {
        var world = level is IDimension child ? child.ParentWorld : level as IWorld;
        return level.DimensionName == "ruby:crystal_world"
            ? world
            : world?.GetLevel("ruby:crystal_world");
    }
});
```

`IWorld.GetLevel(identifier)` finds its root level or a loaded child dimension. Returning null from
`ResolveTarget` disables lighting and travel in that source level. The target can also be a level retained from
`ILevelFactory`. Dimension data is still registered as described in [Dimensions](#dimensions).

`IsFrame` accepts frame materials; `FrameBlock` supplies the material for newly built exits and their safety
platforms. The portal block needs `axis=x,z` states. Frames have optional corners, a 2–21 wide by 3–21 high
interior by default; `MinWidth`, `MaxWidth`, `MinHeight` and `MaxHeight` narrow those limits. Builders use the
minimum width and height. An active portal block belongs to one portal type; vanilla's Nether block is currently
reserved for its built-in type (see [known-gaps.md](known-gaps.md#sharing-vanillas-portal-block)).

The configured `Igniter` is an item used against the frame, with normal survival consumption. More involved item
or block callbacks can call `await level.TryLightPortalAsync(portal, interiorPosition)`, returning whether an empty
frame was lit. Set `IgnitesFromFire` to light frames when fire is placed. Registered portal blocks detect entity
contact, wait, and prepare travel without holding up the entity tick while target chunks generate.

`WaitTicks` defaults to 80, `CreativeWaitTicks` to 1 and `EntityWaitTicks` to 0, matching vanilla's Nether waits.
Like vanilla's portal processor, the previous accumulated time is compared before incrementing it, and time
decays by four per tick out of contact. Successful travel starts a ten-tick player cooldown or a 300-tick entity
cooldown (ten for a vehicle with a player passenger); remaining inside a portal refreshes it.

Coordinate mapping defaults to source dimension type's `CoordinateScale` divided by the target's.
Set `CoordinateScale = (source, target) => ...` for another positive, finite mapping.
`SearchRadius = target => ...` selects 1–128 blocks (128 by default). Existing exits are searched in saved or
loaded chunks, choosing the closest portal block, with lower Y breaking distance ties. Obsidian has no POI index,
so it scans chunk palettes. New exits search within 16 blocks for a clear site and use a frame-material platform
at a safe height if no site exists. Entrance position within the rectangle, axis rotation, velocity and exit
collision adjustment use the same transit path as Nether portals.

The Nether portal is a built-in registration with obsidian, fire/flint-and-steel/fire-charge ignition, the vanilla
shape limits, dimension-type coordinate scales, and searches of 16 blocks into the Nether or 128 on return.
Only that registration names the Nether dimension; general frame, search, building and transit code do not.
End portals (including credits and their arrival platform) and End gateways keep their specialised paths.

Portal types have process-lifetime identifiers and no numeric registry or extra registry sync: clients know their
blocks. Unload removes callbacks and transit behaviour, cancels pending destinations, and leaves placed blocks
alone, including neighbour changes while unloaded. Registering the same identifier after reload returns the same
`PortalType` and reactivates those blocks. In-flight results from a previous registration cannot complete travel.

## Chunk tickets

`ILevel.AddChunkTicket(int chunkX, int chunkZ, int radius = 0, bool ticking = true)` returns an `IDisposable`
handle. Coordinates and radius are in chunks; radius 0 keeps one chunk, radius 1 a 3 by 3 square. Acquire from
plugin code, for example when a machine starts, and dispose when it no longer needs the chunks:

```csharp
var ticket = level.AddChunkTicket(chunkX, chunkZ);
// Later, when this machine stops keeping its chunk active:
ticket.Dispose();
```

- **Loading.** The next chunk-management pass loads the square from disk or queues generation on the existing
  bounded generation workers. The handle does not mean loading is complete: `GetChunkAsync` can still return null
  until generation finishes. Tickets retain chunks through normal unload passes and prevent region eviction.
- **Ticking.** With `ticking: true`, the square uses the player's simulation paths for block entities, scheduled
  block and fluid ticks, random ticks and entities, without needing a player. It also keeps a one-chunk border
  loaded so growth and fluids can reach their neighbours. That border does not itself tick unless a player or
  another ticking ticket covers it. Natural mob spawning and other explicitly player-dependent mechanics still
  need players. A `ticking: false` ticket loads only its square, without a border; it does not suppress simulation
  caused by another ticket or player. Scheduled block and fluid ticks now also respect simulation distance.
- **Limits.** Radius is 0 through 8. Each level allows 256 handles and 1,024 distinct retained chunks, including
  borders. Overlaps count once toward the chunk limit, and every handle counts toward the handle limit. An invalid
  radius or coordinates outside Minecraft's horizontal build range throws `ArgumentOutOfRangeException`; exceeding
  either capacity throws `InvalidOperationException`. Generation may temporarily load extra neighbouring chunks
  needed by its existing pipeline.
- **Threads and ownership.** Acquisition and disposal are safe from plugin tasks and tick callbacks; neither waits
  for chunk loading. Ownership follows the plugin whose code calls the API, including calls through shared levels
  obtained from players or events. Unloading that plugin releases all its handles. Disposal is idempotent, and an
  old load's handles cannot release the new load's tickets. Host code can acquire handles too and must dispose them.
  Release removes simulation eligibility immediately and drops queued generation with no remaining demand. Player
  and explicit world requests remain queued; jobs already generating finish under their existing generation pins.
  Published chunks enter residency bookkeeping immediately, even if their ticket was released before completion.
  Ordinary unloading runs on the existing 30-second cadence.
- **Persistence and parity.** Handles last for the plugin load, not across a restart. A plugin needing persistent
  machines can save their chunk coordinates with `RegisterSavedData` and acquire fresh handles on startup. Unlike
  vanilla's propagated levels (31 entity ticking, 32 block ticking, 33 full), this API has a ticking square and one
  loaded border. Vanilla forced tickets and `/forceload` are deferred; see
  [known-gaps.md](known-gaps.md#chunk-ticket-parity).

`samples/EngineSample` exposes `KeepChunks` to keep a machine active; its smoke test loads and ticks the counter
without players, then unloads the plugin to release even forgotten handles.

## Saved data and attachments

Register types during `ConfigureRegistry` with the same codec shapes as item components:

```csharp
var networks = registry.RegisterSavedData("example:networks", new ComponentCodec<List<int>>(
    values => new NbtArray<int>("", values.ToArray()),
    tag => ((NbtArray<int>)tag).GetArray().ToList()));

var membership = registry.RegisterAttachment("example:membership", new ComponentCodec<int>(
    value => new NbtTag<int>("", value),
    tag => ((NbtTag<int>)tag).Value),
    new ComponentNetworkCodec<int>((writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt()),
    copyOnDeath: true);

var saved = level.GetSavedData(networks, () => new List<int>());
saved.Value.Add(42);
saved.MarkDirty();
player.SetAttachment(membership, 42);
var network = player.GetAttachment(membership);
chunk.RemoveAttachment(membership);
```

`ILevel.GetSavedData(type, create, scope)` gets one live `SavedData<T>` per type and plugin load. The default scope,
`SavedDataScope.Level`, is private to that level. `SavedDataScope.World` shares one instance across its world's
dimensions; `IServer.GetSavedData(type, create)` uses that scope in the default world. A missing file calls `create`
and starts dirty. `Value` is the live plugin object; after changing it, `MarkDirty()` encodes a detached snapshot
immediately. Only that snapshot is saved, so forgetting to mark dirty does not silently save partial changes.

Level files are `<level>/data/<namespace>/<path>.dat`. World-wide files use the world's reserved engine directory,
`<world>/data/obsidian/world/<namespace>/<path>.dat`, to distinguish them from the overworld's own level data. Both
use vanilla's gzip NBT envelope (`DataVersion`, `data`), with the plugin codec's tag under `data`. Names follow the
plugin's namespace, with portable path segments; traversal and Windows device names are rejected, and `obsidian`
is reserved for these types. Saved-data path segments ending in `.dat` or `.dat.tmp` are rejected to prevent
file and directory collisions. Saves replace a temporary file atomically. World saves, region flushes and shutdown
write dirty data. Unloading first flushes any already-published dirty snapshot, without running a codec, then releases
the live instance. World saves leave its file untouched while the plugin is absent; reload reads it again. Old
handles throw and cannot overwrite a new load.

Players and entities implement `IAttachmentHolder` through `IEntity`, and chunks through `IChunk` (obtained from
`ILevel.GetChunkAsync`). `GetAttachment`, `TryGetAttachment`, `SetAttachment` and `RemoveAttachment` are extensions
in `Obsidian.API.Data`. Holders retain encoded NBT only: a get decodes a detached value, and changing that value
requires setting it again. `SetAttachment` and `RemoveAttachment` throw `InvalidOperationException` once a holder
is retired for removal or its final save (chunk unload, entity eviction or player leave). Retirement and publication
share a lock, so a successful change precedes the final snapshot. Reads remain available. Saved holder NBT uses
`obsidian:attachments`, with a value under each type's identifier.
The engine namespace avoids collisions with vanilla tags and does not claim NeoForge save compatibility. Unknown
or unloaded types stay as saved NBT, including through player saves and chunk unloads, until the plugin returns.

Player death respawn drops attachments registered without `copyOnDeath`; kept values are already detached NBT, so
no copy callback is needed. Dimension changes retain all attachments. A type absent during death remains untouched:
an unloaded plugin must not lose its saved player data. `ServerPlayer` follows the same death policy.

A network codec opts a type into sync. The optional, version 1 `obsidian:attachments` play channel carries complete
replacement snapshots, with names and individually framed network values. Only clients negotiating that channel
receive it; vanilla clients receive none, and attachment-only plugins do not extend vanilla registries or prevent
vanilla clients joining. The Obsidian client declares the channel, registers the same type through its mod's
`ConfigureRegistry`, and reads detached values on `IClientWorld` with `TryGetPlayerAttachment(type, out value)`,
`TryGetEntityAttachment(entityId, type, out value)` and `TryGetChunkAttachment(chunkX, chunkZ, type, out value)`.
Players receive their own data, visible players' and tracked entities' data, and data for chunks they have. Changes
and removals follow on the level tick; tracking starts and respawns get fresh snapshots. Client entity removal,
chunk forgetting and
dimension changes clear the relevant snapshots. Unknown client types are skipped without calling their codecs.

**Threads and reload.** Access live saved objects on their owning level's tick. World-wide objects can be reached
from several levels ticking concurrently: give them a plugin-owned lock. Access outside a tick likewise requires
your own synchronization, held through mutation and `MarkDirty`; the engine cannot synchronize a reference after
it has been returned. Factories and persistent codecs run on the caller's thread, inside the plugin call guard.
Attachment reads are detached and holder publication is atomic; synchronize a shared source object before setting
it. Keep codecs short, with no blocking or shared-state side effects. Network encoding runs on the server level
tick; network decoding and all typed client queries run on the client game thread. Disk saves use NBT snapshots
and never call plugins. Unload closes call guards, releases registrations and live saved objects, and rejects late
publication from an old load. Do not retain typed handles across reloads.
Client and server registrations have separate bindings, including when an integrated server shares the process.

The phase 5 `samples/EngineSample` registers independent network ids and membership attachments. Its smoke test
saves player, entity and chunk membership, unloads the plugin, saves again without it, and restores it on reload.

## Entities

```csharp
var golem = registry.RegisterEntity("ruby:golem", new EntityProperties
{
    Width = 1.4f,
    Height = 2.7f,
    MaxHealth = 100,
    Behavior = new GolemBehavior()
});

level.SpawnEntity(position, golem);
```

A plugin's entities are mobs by default: they fall, take damage and run the goals given through
`IEntity.GoalController`. With `Kind = EntityKind.Projectile` they fly along the motion they're spawned with, slowed by
drag and pulled down by `Gravity`, and call `IEntityBehavior.HitAsync` on the first block or living entity in their
way, which removes them; spawn them with `level.SpawnEntity(position, type, motion, owner)`. With `EntityKind.Object`
they fall and rest where they land. Every kind calls the type's behaviour each tick and when a player uses it:
`IEntityBehavior.InteractAsync` returns whether the entity took the interaction, which keeps the held item's
`UseOnEntityAsync` from running, like a vanilla entity's interaction succeeding. (It returned `ValueTask` before; a
behaviour implementing it now returns `ValueTask<bool>`.) The client mod's entity should be of the same kind, so that
their data matches. Entities are saved with the type's identifier; while the plugin is gone, saved ones stay as they
were.

A mob's `Category` (a `MobCategory`, `Misc` by default) is its vanilla mob category. Biomes' `spawners` and biome
modifiers adding spawners (see [Biome modifiers](#biome-modifiers)) spawn a plugin's mobs naturally while the
plugin is loaded, and they count against their category's cap; misc mobs don't spawn naturally, since plugin mobs
don't despawn. They spawn with the rules of the list they're chosen from: on grass in light as creatures, in water in
the water categories, and in the dark otherwise.

`PassengerAttachments` says where riders sit, like a vanilla entity type's passenger attachments: a point per seat,
in blocks from the entity's feet for the entity facing south (+Z ahead, +X to its left), turned with its yaw. The
first passenger takes the first point and those past the last point share it; a player's feet go 0.6 blocks below
it, as vanilla places players. Without points, passengers sit on top of the hitbox, vanilla's default. The server
places a mob's riders there, and the Obsidian client draws riders and the riding player's camera there, so register
the type with the same points on both sides (in `ConfigureRegistry`, which both run). A mob carries as many players
as it has points, at least one.

```csharp
registry.RegisterEntity("ruby:strider", new EntityProperties
{
    Width = 1.2f,
    Height = 1.6f,
    PassengerAttachments = [new VectorF(0, 1.4f, 0.2f), new VectorF(0, 1.4f, -0.6f)]
});
```

### Riding and flying

A player who isn't sneaking mounts a plugin's mob by using it when the behaviour's `CanMount(entity, player)` says
so and `InteractAsync` didn't take the interaction. `IPlayer.StartRiding(mob)` mounts a player from code (any mob
with a free seat), `IPlayer.StopRiding()` takes it off, and `IPlayer.Vehicle` is what it rides; sneaking dismounts,
as in vanilla. The first passenger steers, and the mob's goals pause meanwhile: a walking mob like a horse (its
`MovementSpeed`, a quarter of it backwards, half of it sideways, jumping with jump), and a mob with
`Movement = EntityMovementMode.Flying` like a happy ghast, towards where the rider looks, strafing sideways and rising
with jump, gaining a tenth of its `FlyingSpeed` each tick. Flying mobs have no gravity or fall damage, and their goals
path through the air with Obsidian's flying move control, as vanilla's flying mobs do.

```csharp
var saddled = EntityDataFields.Boolean();

registry.RegisterEntity("ruby:drake", new EntityProperties
{
    Width = 1.4f,
    Height = 1.2f,
    Movement = EntityMovementMode.Flying,
    FlyingSpeed = 0.5f,
    PassengerAttachments = [new VectorF(0, 1.2f, 0)],
    DataFields = [saddled],
    Behavior = new DrakeBehavior(saddled)
});

sealed class DrakeBehavior(EntityDataField<bool> saddled) : IEntityBehavior
{
    public bool CanMount(IEntity entity, IPlayer player) => entity.GetData(saddled);
}
```

The Obsidian client predicts a mob its player rides first, as vanilla's client predicts a horse: it pushes the mob
from the keys like the server does (Obsidian's `MountSteering`, which both share), walking or flying as the type's
`Movement` says, with its synced `movement_speed`, `flying_speed`, `jump_strength`, `gravity` and `step_height`, and
sends where it went with `MoveVehicle`. The server takes those moves after vanilla's checks: a move more than ten
blocks beyond the mob's motion, one ending over a quarter of a block from where the server's collision lets the mob
go, or one into blocks it wasn't in is refused, and the client's mob is put back; a walking mob kept floating for 80
ticks (longer under lower gravity) kicks its rider, as vanilla does. Until the rider's client sends a move, the server
steers from the rider's `PlayerInput` as above, which is what clients that don't predict the mob get. Register the
type with the same `Movement`, speeds and size on both sides, so that both move it alike; modifiers on the mob's
attributes change both sides' movement, since attributes are synced. Fluid currents and the server's mob-specific
travel quirks aren't predicted: the server's checks absorb the small differences.

### Synced data fields

`DataFields` lists a type's synced data fields, like a vanilla entity class's `SynchedEntityData`: make each with
`EntityDataFields` (`Byte`, `Int`, `Long`, `Float`, `String`, `Boolean`, `Item` and `BlockPos`, written with
vanilla's serializers), and read and write them with `IEntity.GetData(field)` and `SetData(field, value)`. A change is
sent on the entity's next tick. The fields follow vanilla's at fixed indices, in their order: from 16 for a mob, after
the entity, living entity and mob fields, and from 8 for a projectile or object; `EntityDataField.Index` says which,
once a type with the field is registered, and a field can't have another index in another type.

Only modded Obsidian clients are sent them: such a client agrees on the optional `obsidian:entity_data` play channel,
which carries no payload, and other clients, whose classes for the type don't have those indices, never see them.
The Obsidian client keeps them in `IClientEntity.Metadata` by index, where entity renderers' texture and layer
providers read them, or with `entity.TryGetData(field, out var value)`; register the type with the same fields on
both sides. Fields aren't saved, as vanilla's synced data isn't: an entity's lasting data belongs in an attachment
(see [Saved data and attachments](#saved-data-and-attachments)), which is saved with it, survives the plugin
unloading, and can sync itself; set the field from it, in the behaviour's `TickAsync` say.

### Attributes

Living entities have vanilla's attributes, vanilla's mobs their type's defaults and a plugin's mobs vanilla's mob
defaults with the type's `MaxHealth`, `MovementSpeed` and `FlyingSpeed`. `IEntity.GetAttributeValue` gives an
attribute's value: its base with its modifiers applied in vanilla's order (`Add` to the base, then multiples of that
sum for `MulBase`, then the result multiplied for each `MulTotal`), clamped to vanilla's range for it.
`IEntity.SetAttributeModifier(attribute, id, amount, operation)` adds or replaces a modifier, like vanilla's transient
modifiers (not saved), and `RemoveAttributeModifier` takes it off. Players get the modifiers of what they hold and
wear (items' `AttributeModifiers`, a plugin item's default included) as their equipment changes, and vanilla's
sprinting modifier. Clients are sent the attributes vanilla syncs with `UpdateAttributes`: all of a living entity's
when it comes into view, the player's own after joining and respawning, and those that change, to the entity's
watchers and a player itself.

### Damage, death and loot

`IEntityBehavior.HurtAsync(entity, damage)` runs when a plugin's mob is about to take damage, after its immunities and
before its hurt cooldown, armour and absorption, like NeoForge's `LivingIncomingDamageEvent`: change `damage.Amount`,
or set `damage.Cancelled`. `damage.Source` is a `DamageSource`: vanilla's damage type (`minecraft:player_attack`,
`minecraft:mob_attack`, `minecraft:arrow`, `minecraft:fall` and so on), the attacker and the direct entity.
`DiedAsync(entity, source)` runs when it dies, before its loot drops. Both run inside a call into the plugin, never
after it unloads.

A dying plugin mob drops its type's loot table, `data/<namespace>/loot_table/entities/<path>.json` for
`<namespace>:<path>` (a data pack can override it), like vanilla's `dropFromLootTable`, when the `mob_drops` rule is
on. Its context has the mob, the attacker and the player who hurt it in the last 100 ticks, whose luck counts, so
`minecraft:killed_by_player`, `minecraft:random_chance_with_enchanted_bonus` and
`minecraft:enchanted_count_increase` (looting, from the killing weapon) work like vanilla's.

```json
{ "type": "minecraft:entity", "pools": [{
  "rolls": 1,
  "conditions": [{ "condition": "minecraft:killed_by_player" }],
  "entries": [{ "type": "minecraft:item", "name": "ruby:drake_scale", "functions": [
    { "function": "minecraft:enchanted_count_increase", "enchantment": "minecraft:looting", "count": 1 }
  ] }]
}] }
```

### Spawn placement

`SpawnPlacement` gives natural spawning a type's own rules, like vanilla's spawn placements: `OnGround` (on a block
that holds it up, no fluid at its feet), `InWater` (in water, with water or air above), `InLava` or `NoRestrictions`,
each with its hitbox free. Natural spawning then skips the spawn list's light and floor rules and asks the
behaviour's `CanSpawn(level, position)`, which can check light, blocks or anything else, on the level's tick. Without
a placement a mob follows its spawn list's rules, as above. Spawn eggs for plugin mobs aren't provided.

## Movement

Attribute modifiers (see [Attributes](#attributes)) tune the movement players and mobs already have:
`minecraft:jump_strength`, `minecraft:gravity`, `minecraft:movement_speed`, `minecraft:step_height`,
`minecraft:safe_fall_distance` and the rest are synced, so the server and every client move alike. For moves no
attribute gives, a plugin grants a player `MovementAbilities` with `IPlayer.SetMovementAbilities(id, abilities)`:

- `ExtraJumps`: jumps in the air before landing, each a new press of jump, as high as a ground jump, forgiving the
  fall so far (a double jump is `ExtraJumps = 1`);
- `CanGlide`: gliding like with an elytra, without one: a press of jump in the air starts it, landing ends it;
- `CanFly`: creative-style flight in any game mode (vanilla's `mayfly`).

```csharp
// Boots of the clouds: a double jump while they're worn, from an equipment change handler, say.
player.SetMovementAbilities("ruby:cloud_boots", new MovementAbilities { ExtraJumps = 1 });

// Taken off.
player.RemoveMovementAbilities("ruby:cloud_boots");
```

Grants with different ids add up (their extra jumps sum, the rest combine), and the same id replaces its grant.
`IPlayer.MovementAbilities` is the total. Grants aren't saved, and unloading the plugin that made them takes them
back, flight included. They come from the keys vanilla already sends (jump, and the input packet the server reads),
so a mod needs no input of its own; a mod wanting its own key for a move binds it on the client and sends a payload
(see [Client](#client)), whose server handler grants the ability.

The server doesn't simulate players: like vanilla, it takes the client's positions, so it keeps its own state in step.
It counts a press of jump in the air as an extra jump while one is left (forgiving the fall, which fall damage
reads), starts gliding when the client asks if the player has a glider on the chest or `CanGlide`, ends gliding on
landing, mounting, flying or losing both, and lets a survival player fly while `CanFly` (it no longer kicks a
client flying where flight is allowed). Flight works with every client, through vanilla's abilities packet. Extra
jumps and gliding without a glider need the Obsidian client, which is sent the abilities on the optional
`obsidian:movement` play channel and predicts them; a vanilla client just doesn't jump twice or glide without one.

## Block entities

```csharp
public sealed class Counter : BlockEntity
{
    private int ticks;

    protected override void Load(NbtCompound tag) =>
        this.ticks = tag.TryGetTagValue<int>("ticks", out var ticks) ? ticks : 0;

    protected override void Save(NbtCompound tag) => tag.Add(new NbtTag<int>("ticks", this.ticks));

    protected override ValueTask TickAsync()
    {
        this.ticks++;
        this.SetChanged();

        if (this.ticks % 20 == 0)
            this.SendUpdate();

        return ValueTask.CompletedTask;
    }

    protected override NbtCompound? GetUpdateData() => new() { new NbtTag<int>("ticks", this.ticks) };
}

var counter = registry.RegisterBlock("ruby:counter");
registry.RegisterBlockEntity<Counter>("ruby:counter", counter);
```

A block entity type, like a NeoForge mod's `BlockEntityType`, gives the plugin's blocks a `BlockEntity`: the server
creates one whenever one of its blocks is set, however it's set (placed by a player, by plugin code, by world
generation or a structure), and removes it when its position changes to a block without its type. Removing it calls
`OnRemoved` before a container's items drop, like vanilla's `preRemoveSideEffects`. The type's identifier is in the plugin's namespace, and its
id follows vanilla's in the `minecraft:block_entity_type` registry, synced to NeoForge and Fabric clients, whose mod
registers the same type for the same blocks. The Obsidian client uses the same registration code, retaining the
type and its update data from chunk packets and `BlockEntityData`, and remapping server ids by identifier.

- **Saving.** The block entity is saved in its chunk's `block_entities` with the type's `id` and its position, and
  `Load` reads back what `Save` wrote. Call `SetChanged` whenever the saved data changes, like vanilla's
  `setChanged`: the level calls `Save` at the end of its tick, and saves the chunk with what it wrote.
- **Ticking.** `TickAsync` runs once a tick while the chunk ticks (a player is within the simulation distance), and
  only while the block still has the type. `Level` is the level it's in, and `BlockPosition` its block. Clients tick
  their own instances with `ClientTick` (see **Client ticks**).
- **Clients.** `GetUpdateData` is what clients get (vanilla's `getUpdateTag`): with the chunk, and at the end of the
  tick after the block entity calls `SendUpdate` (once however many times it called it), as a `BlockEntityData`
  packet to the players who have the chunk. Attachment and restoration also queue an initial update, with the block
  state sent before its data. The Obsidian client retains that data as a snapshot, which a client shape provider
  reads (see [Client](#client)), and keeps a live instance of the mod's class for client ticks (below).
- **Client ticks.** On the Obsidian client, a mod's block entity gets a live instance of its class, created with
  the type's registration when the server sends it (with the chunk or `BlockEntityData`) and loaded with `Load`
  from the update data rather than saved data: again, on the same instance, on each update. Override `ClientTick`
  for what only players see (animations, particles, sounds), like vanilla's client-side tickers: it runs once a
  client tick, 20 times a second, on the game's thread while the player is in a world that isn't paused, never on
  the server. It may use the block entity's own fields, `Id`, `BlockPosition` and client state the mod kept from
  `ConfigureClient`. With `using Obsidian.API.Client`, `this.GetClientWorld()` supplies the client's read-only
  world view during `Load` and `ClientTick` (see **World queries** in [Client](#client)). `Level` stays the server
  level and is null on the client; `SetChanged` and `SendUpdate` do nothing. The instance is dropped
  (`IsRemoved`) when its block changes to one without its type, its chunk unloads, the player leaves the server or
  the mod unloads. A tick or load that throws is logged, and the instance isn't ticked again until the next update.
  Shapes and rendering keep reading the snapshot, from their own threads, never the live instance.
  `Load` receives its own copies of update arrays, including nested arrays. Removing and recreating a block entity
  creates a fresh live instance even when both packets arrive between client ticks; a data update retains the
  instance.
- **Items.** Like vanilla's `collectImplicitComponents` and `applyImplicitComponents`, a block entity can keep its
  data on its block's item: `CollectComponents(ItemStack item)` sets values of the plugin's own component types (see
  [Item components](#item-components)) on a stack of the block's item as the block breaks, by a player or an
  explosion, and `ApplyComponents(ItemStack item)` reads them back when a player places the block with that item.
  What `CollectComponents` sets goes to the block's loot like a vanilla block entity's components: a block without a
  loot table drops its item with the plugin's component values, and a loot table copies them with
  `copy_components` (`"source": "block_entity"`, `"include": ["ruby:ticks"]`), as the engine sample's tick counter
  does. `ApplyComponents` runs on the new block entity before anything else reaches it, on the placing thread, with
  `Level` still null; the server saves the block entity after it (like vanilla's `setChanged` there). A hook that
  throws is logged, and the drop or the block goes without the data. Neither runs once the plugin starts unloading:
  a block entity kept as saved data while its plugin is gone gives its item nothing, and keeps its data. A
  container's items still drop as its items. Creative pick-block with data and `block_entity_data` aren't supported.
- **Server.** `Server` is the plugin's own `IServer` in the calls into the plugin, resolved like event arguments'
  `Server`: a machine finds its plugin's recipes with `this.Server?.FindRecipe(type, inputs)`. It's null while the
  block entity isn't in a level (in `ApplyComponents`, say).
- **Threads.** The server calls `Load`, `Save`, `TickAsync`, `GetUpdateData`, `CollectComponents` and `OnRemoved`
  on the level's tick, one at a time, inside calls into the plugin: a block removed from elsewhere (a player's break)
  runs `OnRemoved` on the next tick, and a break elsewhere (an explosion a player sets off with a bed, say) runs
  `CollectComponents` between two ticks, once the running tick ends. A background task waits the same way, even when
  the tick started it. Only a new block entity for a block set
  elsewhere is constructed and given its item (`ApplyComponents`) there. Chunks are saved and sent from copies of what
  those calls returned last, so the block entity's code needs no locks of its own, and changing a compound it handed
  over changes nothing saved or sent.
- **Generation.** In chunks still generating, and in chunks being loaded, a plugin's block entities are kept as
  their saved data: the level creates the plugin's objects on its next tick once the chunk is complete.

### Shapes and multipart blocks

Override `BlockEntity.GetShapes()` to return a `BlockShapes(collision, outline)`, two lists of `BoundingBox` in
block-local coordinates. Each box must have positive volume and finite coordinates inside the unit cube, from
0 to 1 on every axis. The lists are copied and read-only. Empty lists mean no collision or no selection;
returning null keeps the state's usual shapes (a plugin block's template). Collision and outline may differ.

The server publishes the shapes when it creates or loads the block entity and at the end of a tick after
`SetChanged` or `SendUpdate`, and immediately after each player's break hook so the next attempt sees the remaining
parts. Collision, navigation and outline raycasts read that immutable snapshot without
calling plugin code from their threads. A shape change that clients must see calls `SetChanged` and `SendUpdate`,
and includes its deciding data in `GetUpdateData`. The client registers a provider that builds the same shapes
from that data. Unloading drops both sides' behaviour, keeping the saved data and type id for the next load.

`BlockUseContext.ClickedAt`, like `BlockPlaceContext.ClickedAt`, is the use packet's hit location relative to the
clicked block's lowest corner. `IBlockBehavior.BreakAsync(BlockBreakContext)` runs before a player's block removal
and drops, after the existing break event has allowed it. Its `Player` and `Block` identify the attempt, and its
`Hit` is a `BlockShapeHit(Position, ClickedAt, Face, BoxIndex)` from a server outline raycast within the player's
block interaction range. The nearest intersected box decides `BoxIndex`, in the returned outline list's order;
another block in the way, a miss or an out-of-reach target prevents the hook and break. Vanilla's break packet
has no hit location, so neither its face claim nor client-provided coordinates decide this hit.

Set `BlockBreakContext.IsCancelled` to keep the block after removing one selected part, then mark and sync the
block entity's change. Leave it false for ordinary removal when the last part goes. The client receives a block
acknowledgement, state correction and retained block entity data on cancellation. Target validation, the hook and
the resulting break transaction run together on the level's tick, in turn with block entity callbacks, inside a
call into the plugin. The outline ray starts at the player's pose- and scale-dependent eye position.

```csharp
public async ValueTask BreakAsync(BlockBreakContext context)
{
    if (await context.Player.Level.GetBlockEntityAsync(context.Hit.Position) is CableBus cable
        && cable.RemovePart(context.Hit.BoxIndex))
    {
        cable.SetChanged();
        cable.SendUpdate();
        context.IsCancelled = true;
    }
}
```

A `ContainerBlockEntity` holds a `BaseContainer` (its `Inventory`), saved like vanilla's containers (`Items`,
`CustomName`, and a `LootTable` it's waiting for), which drops when the block is removed unless `DropsContents` is
false. Changes to its items are saved without `SetChanged`, under the inventory's click lock, so that a save never
has a click half applied or a loot table half rolled. Every chunk save takes current container fields directly,
including for remote menus outside simulation distance, while plugin-specific fields keep their tick snapshots.
Loot is prepared in a separate inventory without the tick, item transfer, click or loot locks; saves keep the
pending table until the server revalidates and publishes the complete fill under the click and loot locks.
When the block goes, its items are taken out under that lock and the inventory is retired: the windows showing it
close, and it takes no more clicks and doesn't open again. Vanilla's block entities work the same way: chests,
barrels, dispensers, droppers, hoppers, shulker boxes, furnaces and brewing stands are `ContainerBlockEntity`s, so
`await level.GetBlockEntityAsync(position) is ContainerBlockEntity chest` reaches a chest's items, an unopened one's
too (its `LootTable` is rolled when a player opens it or it's broken), and the others keep vanilla's saved data. Like
vanilla's, containers drop their items when their block goes by any cause, and brewing stands brew on the block
entity tick.

When the plugin unloads, its block entities are saved into their chunks as of their last change (one not taken in
yet is saved then) and let go of; nothing of the plugin's runs after that, and the plugin isn't released before. A
container's inventory is retired first, under its lock, and the windows showing it, or any container of the plugin's
own classes, close: no player takes out an item the reload brings back. Chunk and region eviction also retire
inventories before their final save and close remote viewers after persistence releases the region save semaphore,
so close handlers may flush that region. The server saves a plugin container's size and drop
policy as `obsidian:container` data, so its items and pending loot still have container removal semantics while the
plugin is absent. Removing a container kept as data spills its contents even when its saved `DropsContents` is false:
the absent plugin cannot produce its content-keeping loot drop. The host supplies no `container` component to that
block's loot, so a remaining table cannot also copy those items. While it's gone they're kept as they were saved,
in loaded chunks and in chunks loaded meanwhile,
like saved entities of its types; once it loads again, the level creates them again from what they saved on its next
tick, and so do chunks loaded later.

### Item access

```csharp
// A machine that takes ore from the side it's fed from, and gives its output from below.
public sealed class Crusher() : ContainerBlockEntity(new Container(9) { Title = "Crusher" })
{
    protected override IReadOnlyList<int> GetSlots(BlockFace? side) => side switch
    {
        BlockFace.Down => [8],
        null => base.GetSlots(side),
        _ => [0, 1, 2, 3]
    };

    protected override bool CanInsert(int slot, ItemStack stack, BlockFace? side) =>
        side is null || slot < 8 && stack.Is(ItemsRegistry.IronOre);

    protected override ValueTask TickAsync()
    {
        // Push one item a tick into whatever is below, through its top face, planning both ends first.
        var below = this.Level?.GetItemAccess(this.BlockPosition + Vector.Down, BlockFace.Up);
        var output = this.GetItemAccess(side: null)!;

        if (below is not null
            && output.Extract(8, 1, simulate: true) is ItemStack planned
            && below.Insert(planned, simulate: true) is null
            && output.Extract(8, 1) is ItemStack taken
            && below.Insert(taken) is ItemStack refused)
        {
            // A player's click came between the plan and the move: put back what was refused.
            output.Insert(8, refused);
        }

        return ValueTask.CompletedTask;
    }
}
```

`IItemAccess`, like NeoForge's item handler capability, is one way to reach the items of whatever is at a position,
from a side: `ILevel.GetItemAccess(position, side)`, or `IBlockLevel.GetItemAccess` inside a block hook, both
synchronous (they read loaded, complete chunks and never load one). The side is the face of that block the caller
reaches through: a hopper above a furnace reaches it through `BlockFace.Up`; null means no particular side, like
the block's own code. It has `SlotCount`, `GetItem(slot)`, `Insert(slot, stack)`, `Insert(stack)` (into the slots in
order, like a hopper) and `Extract(slot, count)`. Inserting returns what didn't go in and extracting what came out
(null for nothing); stacks passed in aren't changed and stacks returned are the caller's. Every call takes
`simulate`: simulated, it reports what it would do and changes nothing, so a pipe or network plans both ends of a
transfer before doing either. Each call is atomic, but two calls aren't a transaction, so check what the real call
returns: a player's click can come between them.

For a machine's conversion inside its own inventory, use the protected `IDisposable? LockInventory()` in a
synchronous part of `TickAsync`. A null handle means the inventory is unavailable (removed, retired or pending
loot), or the caller holds the level's tick lock. With the handle held, read and change `Inventory` directly:
match the current ingredient, check the output's space, consume the ingredient, then write the result, all before
disposing the handle. It takes the same lock as menu clicks and notifies viewers and comparators when the items
change. Do not await, call item access (even your own), or take other locks while holding it. Transfers to another
container still use item access outside this scope and check the real extraction and any refused insertion.
The sample grinder demonstrates this pattern, resets progress when its recipe changes, and saves the recipe's
identity and settings with its progress. Saved progress without matching recipe data starts again from zero.

Slots are the side's: a furnace's top reaches only its ingredient slot, which is slot 0 of that access. Vanilla's
containers follow vanilla's `WorldlyContainer` rules. Chests, trapped chests, barrels, hoppers, droppers and
dispensers give every slot to every side, and a double chest is one 54-slot inventory, its right half first.
Furnaces, smokers and blast furnaces give the ingredient to the top, the fuel to the sides, and the result then the
fuel to the bottom; nothing goes into the result, only fuel (or one bucket) into the fuel slot, and only a bucket
comes out of the fuel slot from below. Brewing stands give the ingredient to the top, the bottles and ingredient to
the bottom, and the bottles and fuel to the sides, with one bottle a slot and only a glass bottle leaving the
ingredient slot through a face. Shulker boxes take anything but shulker boxes. A container still waiting for its
loot table rolls it first, as vanilla's hoppers do.

A `ContainerBlockEntity` gives this access from every side to every slot. Override `GetSlots(side)`,
`CanInsert(slot, stack, side)` and `CanExtract(slot, stack, side)` for per-side and per-slot rules (they run with the
inventory's lock held, so read `Inventory` and don't wait on anything), or override `GetItemAccess(side)` to give
your own. Any other block entity exposes items by implementing `IItemAccessProvider`, and a block without a block
entity, like vanilla's composter, by returning one from `IBlockBehavior.GetItemAccess(ItemAccessContext)`, which
is asked first. A plugin's access is called inside calls into its plugin, from any thread, and gives nothing once
the plugin is unloading; make it thread-safe. A container block entity holds at most `ContainerBlockEntity.MaxSlots`
(256) slots, since vanilla saves a slot's number as a byte; a bigger inventory throws when it's constructed.

A container's access shares its click lock with players' windows, so a call never sees a click half applied, and it
takes and gives nothing once the block entity is removed (its inventory is retired), so a hopper can't move items in
or out of a broken chest. A change calls `SetChanged`, and at the end of the level's tick the comparators reading
the container update and the players with it open get its new contents. Inside a block hook, which holds the level's
tick lock, a container busy with a click at that moment, or still waiting for its loot table, takes and gives nothing
for that call (its loot is rolled on the next tick), so that a hook never waits on a click or loads a chunk.

Hoppers, droppers and dispensers move items through it, so they reach plugins' blocks like vanilla's. A hopper ticks
with its block entity like vanilla's: when it's off cooldown and not powered, it pushes one item, from its first
slot that has one that fits, into the block it faces, then pulls one from the first slot the block above gives up
(or takes in the item entities in its bowl and the block above, when there's no container there and it isn't a full
block), and waits 8 ticks after moving anything. Items reaching an empty hopper hold its next transfer back 8 ticks,
7 when another hopper that ticked no later pushed them, so chains move at vanilla's pace whatever order hoppers tick
in. A dropper or dispenser fires 4 ticks after a redstone signal reaches it or the block above, picking a slot like
vanilla; a dropper puts the item into the block it faces when it has item access (keeping it when it doesn't fit),
and otherwise both drop it.

## Particles

```csharp
var spark = registry.RegisterParticle("ruby:spark");

level.SpawnParticles(spark, position, count: 8, spread: new VectorF(0.2f), speed: 0.05f);
```

A plugin's particle type is a particle without options, like a NeoForge mod's `SimpleParticleType`; the
`ParticleDefinition` that `RegisterParticle` returns is the particle to send. `ILevel.SpawnParticles` sends any
particle, vanilla's particle data too, like vanilla's `/particle`: to players closer than 32 blocks (512 with
`force`) in any direction, vertically too, with a count of 0 as one particle moving along `spread` times `speed`,
and otherwise as `count` particles scattered by up to about `spread` on each axis. The type keeps its id across
reloads and stays registered until the server stops. Clients draw it: a NeoForge or Fabric mod registers a particle
type with the same identifier and a provider, and the Obsidian client draws it with the mod's sprites (see
[Client](#client)).

## Sounds

Declare sound events in `ConfigureRegistry` on both sides. `RegisterSound(string identifier, float? fixedRange = null)`
returns a `SoundEvent` for the existing `IPlayer.SendSoundAsync` API and for local client playback:

```csharp
var chime = registry.RegisterSound("ruby:machine.chime", fixedRange: 24);
var effect = SoundEffectBuilder.Create(chime, SoundCategory.Blocks)
    .WithSoundPosition(new SoundPosition(4.5, 70.5, 4.5))
    .WithVolume(0.7f)
    .WithPitch(1)
    .Build();
await player.SendSoundAsync(effect);
```

The identifier must be in the plugin's namespace. A fixed range is a finite, nonnegative number; null means variable
range. Unloading removes the declaration and registering it on reload replaces its range under the same identifier.
The returned value is a snapshot: obtain it again during each load. No numeric ids are assigned or synced. Vanilla
1.21.11's sound holders permit zero followed by the identifier and optional range, and both positional and entity
sound packets already use this inline form. A sound-only plugin therefore allows vanilla clients, which hear the
event if their resource packs provide it, and otherwise ignore it. Unlike this path, NeoForge registers and syncs
`minecraft:sound_event` to send registry references; see `NeoForgeRegistriesSetup.java` in its
[registry setup sources][sound-sync].
Mod blocks still require matching client code as before.

Set `BlockProperties.SoundType` to a `BlockSoundProperties`. `Break`, `Step`, `Place`, `Hit` and `Fall` are sound
events; `Volume` and `Pitch` default to 1. Build it with vanilla identifiers, registered events, or a mix:

```csharp
registry.RegisterBlock("ruby:machine", new BlockProperties
{
    SoundType = BlockSoundProperties.Stone with
    {
        Break = chime,
        Place = chime,
        Volume = 0.7f
    }
});
```

Declare the same type on both sides. Vanilla block sounds come from the captured per-state sound types; mod blocks
use their declaration for the client's hit, step, placement and break prediction. The server sends placement and
break sounds to nearby players, excluding the acting player whose client predicts them. Unloading resets mod blocks
to stone sounds and reloading replaces them. `Fall` is available on the type; landing audio awaits the client's
landing-sound behavior.

In `ConfigureClient`, keep `IClientRegistry.Sounds`, an `IClientSounds`. After configuration, call
`Play(sound, position, category = SoundCategory.Master, volume = 1, pitch = 1)`
or `PlayUi(SoundEvent sound, float volume = 1, float pitch = 1)` from screens, client ticks or payload handlers. Calls
may run on any thread: the facade queues playback on the game thread; asset I/O and OpenAL use the engine's workers.
UI audio is non-positional, plays while the world is paused, and works without a server connection. Calls return false
while the mod is inactive or audio is unavailable; true means queued, and missing assets remain silent. Unload disables
the old facade and cancels requests from that load through asset loading and backend dispatch; get the new facade
when configuring again.

Supply `assets/<namespace>/sounds.json` and `assets/<namespace>/sounds/<path>.ogg` in the mod archive, like vanilla
resource packs. Definitions merge with vanilla and player packs, including `replace`, event references and subtitles;
an archive needs no `pack.mcmeta`. `samples/EngineSample` declares `sample:counter.chime`, referring to vanilla's
amethyst chime in its `sounds.json`, and uses it for its counter's placement and break sounds.

[sound-sync]: https://github.com/neoforged/NeoForge/tree/1.21.11/src/main/java/net/neoforged/neoforge/registries

## Menus

```csharp
var pouch = registry.RegisterMenu("ruby:pouch", rows: 1, columns: 5);

var container = new MenuContainer(pouch, "Ruby pouch");
container.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 3));
await player.OpenInventoryAsync(container);
```

A plugin's menu type is a grid of 1 to 6 rows of 1 to 9 slots above the player's inventory, numbered row by row and
followed by the player's main inventory and hotbar (`MenuContainer.PlayerSlotCount`, 36) like vanilla's menus. The
server opens it with its id and moves items in it like a chest's; a `ContainerClickEventArgs` handler cancels or
handles players' clicks, and the plugin reads and changes the container's slots. Several players can have the same
container open: their clicks take turns, so two can't take the same stack, and each sees the others' changes. A
NeoForge or Fabric mod's menu type with the same identifier and slots shows it, and the Obsidian client shows it like a
chest or with the mod's screen (see [Client](#client)). The type keeps its id and shape across reloads and stays
registered until the server stops; registering it again with another shape throws. When the plugin unloads (or
reloads), its menus close for the players who have them open, with a `ContainerClosedEventArgs` the plugin still
handles although it takes no other calls. From then until a loaded plugin registers the type again, opening one of
its menus does nothing. A client that wasn't sent the type with its registries (one still to go back through
configuration after the plugin registered it) doesn't get the menu either: opening it does nothing.

### Machine menus

```csharp
// A crusher's menu: ore in its inventory, its own buffer behind item access (an IItemAccess), progress and a button.
public sealed class CrusherMenu : MenuContainer
{
    private readonly Crusher crusher;

    public CrusherMenu(MenuDefinition menu, Crusher crusher) : base(menu, "Crusher")
    {
        this.crusher = crusher;
        this.Slots =
        [
            new MenuSlot(crusher.Inventory, 0) { MayPlace = item => item.Is(ItemsRegistry.IronOre) },
            new MenuSlot(crusher.Buffer, 0) { MaxStackSize = 16 },
            new MenuSlot(crusher.Inventory, 8) { MayPlace = _ => false }
        ];
        this.BlockEntity = crusher;
        this.DataSlots = 1;
    }

    public override short GetData(int index) => (short)this.crusher.Progress;

    // Ore goes to the input, anything else to the buffer; the menu's stacks go back to the player.
    protected override IReadOnlyList<MenuSlotRange> QuickMove(IPlayer player, int slot, ItemStack stack) =>
        slot < this.Size
            ? base.QuickMove(player, slot, stack)
            : stack.Is(ItemsRegistry.IronOre)
                ? [new MenuSlotRange(0, 1)]
                : [new MenuSlotRange(1, 2)];

    protected override bool ClickButton(IPlayer player, int button)
    {
        this.crusher.Paused = button == 0;

        return false;
    }

    protected override void WriteOpenData(IPlayer player, INetStreamWriter writer) =>
        writer.WritePosition(this.crusher.BlockPosition);
}

var crusherMenu = registry.RegisterMenu("ruby:crusher", rows: 1, columns: 3);
await player.OpenInventoryAsync(new CrusherMenu(crusherMenu, crusher));
```

A `MenuContainer` keeps its slots' items itself unless `Slots` says where each of the type's slots is kept, like
vanilla's `Slot` and NeoForge's `SlotItemHandler`: in a container's slot (`new MenuSlot(container, index)`), behind
item access (`new MenuSlot(itemAccess, index)`), or in the menu itself (`new MenuSlot()`). Each slot has its own rules:
`MayPlace` (which stacks players may put in it; a slot that takes nothing, like an output, still gives its stack whole
to a cursor of the same item), `MayPickup` (whether a player may take its stack) and `MaxStackSize` (up to each item's
own). The rules run as clicks move items, with the window's lock held, so they must not wait. Where the slots go on
screen is the client's business: a mod's client side gives their positions with `MenuScreenProperties` (vanilla's
server doesn't use slot positions either).

A slot kept in a container is that container's slot: a click changes it under the container's lock, which it takes
with the others in one fixed order, so that a menu made of several containers takes turns with the players who have
them open, on their own or in other menus, and they all see its changes. A slot behind item access, like a plugin
machine's buffer, moves its items through the access as the click ends: what the click took out is extracted first,
and if a slot no longer holds it (a pipe took it meanwhile), the whole click is undone; then what it put in is
inserted, and what the access refuses goes back to the player's inventory, or drops. If an access callback throws,
successful transfers are undone too; items the access cannot take back are refunded to the player. Player drops and
refunds belong to the host and survive the menu plugin unloading. Each access call must be atomic, including on
failure. The access is called with the menu's locks held: a container block entity's access takes and gives nothing
while its container is busy rather than wait, and your own access mustn't wait on a container's lock either. Don't
show a container's slots both ways in one menu. Outside a click, change such a slot through its access;
`SetItem` throws. Players with the menu open see the access's changes on their next tick.

Subclass `MenuContainer` for the rest, like NeoForge's `AbstractContainerMenu`. These run inside calls into the plugin
that registered the menu type, never once it's unloading. Each menu instance remains bound to that plugin generation;
registering the type again does not reactivate an old instance:

- `QuickMove(player, slot, stack)` decides shift-clicks, like vanilla's `quickMoveStack`: it returns ranges of the
  window's slots (the menu's from 0, then the player's 36), tried in turn until one takes some of the stack. Each tops
  up stacks of the same item first, then fills its first empty slot that takes the item, following the slots' rules,
  from its end when `Reverse` is set. By default the menu's stacks go to the player's slots, filling from the hotbar's
  last slot, and the player's go to the menu's slots, filling from the first, like a chest.
- `DataSlots` and `GetData(index)` are vanilla's `ContainerData`: short values like a furnace's progress. By default
  `GetData` returns what `SetData` set; override it to read the value from what it shows. The players who have the
  menu open get them all as it opens, then, once a tick, the ones that changed (vanilla's `ContainerSetData`).
- `ClickButton(player, button)` handles a button of the mod's screen, like vanilla's `clickMenuButton`: the client
  sends a number, which means whatever the menu says. It runs with the window's lock held, like a click (spectators'
  clicks don't reach it); return true when it changed the slots, so that the viewers are sent them.
- `WriteOpenData(player, writer)` writes extra data as the menu opens, like NeoForge's `openMenu` with a buffer:
  NeoForge clients and the Obsidian client get it with NeoForge's `neoforge:advanced_open_screen`, which they read to
  build their side of the menu. Other clients get vanilla's open screen packet, which carries none.
- `BlockEntity` binds the menu to a block entity, like a machine's: it closes when the block entity is removed (its
  block broken or replaced, its chunk unloaded or its plugin unloaded), with the windows showing a container block
  entity's inventory or on the player's next tick, and, like vanilla's `stillValid`, once the player's eyes are
  farther than their block reach plus 4 blocks from its block. Clicks in it stop applying as soon as it's removed.
  `StillValid(player)` adds a check of your own, made every tick and on every click.

### Click handlers

```csharp
[EventPriority(Priority = Priority.Normal)]
public ValueTask OnClick(ContainerClickEventArgs click)
{
    if (click.Container is not MenuContainer { Menu.Identifier: "ruby:pouch" } pouch || click.ClickedSlot != 0)
        return ValueTask.CompletedTask;

    // Selling the diamonds in the first slot: decided on what the slot held as the click arrived.
    if (click.Item?.Is(ItemsRegistry.Diamond) == true)
    {
        click.Cancel();
        click.ApplyWithClick(() => pouch.SetItem(0, new ItemStack(ItemsRegistry.Emerald, click.Item.Count)));
    }

    return ValueTask.CompletedTask;
}
```

Click handlers, like every container's (chests, furnaces, the player's inventory), run before the server applies the
click and outside the container's lock, so a handler may wait, even for something another viewer does. The server's
own handler runs last: inside the lock it checks that the window is still open and that neither the container, the
player's inventory nor the cursor changed since the click arrived (a slot set, or a stack in one changing its count or
components), then applies the click unless it's cancelled, and sends the window's contents and the cursor to its
viewers.

- `Item` is a copy of what the clicked slot held as the click arrived, made each time it's read; it shares nothing with
  the slot's mutable stack state. Immutable component values can be shared safely; changing the copy changes nothing.
- A click whose slots changed while handlers decided isn't applied, so a handler's decision never applies to contents
  it didn't see. Like a cancelled click, it's corrected: the player's window and cursor are sent again. In a crafting
  window, a click whose result slot no longer shows what the grid crafts (the grid or the recipes changed without the
  result following) is corrected the same way, rather than taking a result worked out again.
- To change slots with the click, register the change with `ApplyWithClick`: it runs inside the lock right after the
  click (or in its place, when cancelled), only if the click isn't stale, and the viewers see its result, a crafting
  result following the grid included. It must not block or wait.
- A handler may change the container directly, through its methods (`SetItem`, `RemoveItem`), but that change isn't
  part of the click: it makes the click itself stale, and may race other viewers' clicks. So does changing a stack it
  holds (its count, or a component set with the stack's indexer, `Add` or `Remove`). Component values have init-only
  properties and immutable collections. Replace a value with `stack[food.Type] = food with { Nutrition = 5 };`.
  For a collection, assign a new collection, such as `model with { Floats = model.Floats.Add(1f) }`, through the
  stack's indexer. The container's revision advances without replacing the stack or polling its component values.
  Legacy mutable payloads (NBT, chat messages and nested stacks) are defensive copies when set or retrieved;
  after editing such a copy, assign it in a replacement component. `DataComponent.Read(reader)` also returns a
  new value: use its return value. Custom component types must keep their payloads immutable too.
- A `ContainerClosedEventArgs` handler may open another container for the player; the server then closes only the
  window the event is about.
- What the server sends about a window (a click's contents, the slots an item or arrow picked up fills) goes through
  `QueuePacketEventArgs` handlers like any packet, each packet on its own even when it's then bundled with others
  (an item pickup's animation with its slots). One that the window outdated while they ran isn't sent; the window
  is captured again instead, and if it keeps changing, the player's next ticks send it whole, through the handlers
  too, once it holds still.

## Recipes

```csharp
server.RegisterRecipes(ShapedRecipeBuilder.Create(CraftingBookCategory.Building)
    .WithKey('R', new ItemStack(ruby, 1))
    .WithPattern("RRR", "RRR", "RRR")
    .HasNoGroup()
    .WithIdentifier("ruby:ruby_block")
    .WithResult(new ItemStack(rubyBlock, 1))
    .Build());
```

Recipes registered with a plugin's `IServer.RegisterRecipes` join vanilla's in that server's recipe set. Other
servers in the same process do not share the additions. Registration copies ingredients and result stacks, so
changing the original recipe afterward has no effect:

- Crafting tables and the player's 2x2 grid craft shaped and shapeless ones. Vanilla's recipes are tried first, then
  the others in the order they were first registered, so a plugin's recipe can't take over a vanilla pattern.
- Every player's recipe book shows them, and placing one from the book fills the grid, or shows its ghost when the
  player lacks the items. Obsidian has no recipe unlocking: every player knows every crafting recipe.
- Furnaces, blast furnaces and smokers cook smelting, blasting and smoking recipes with their recipe's cook time,
  vanilla fuel durations (halved in the fast machines), lit state and menu progress. Cooking progress, remaining
  fuel and recipe-use counts survive saving. Taking output awards the accumulated experience, including output
  extracted earlier by hoppers. History for a temporarily missing recipe stays saved until it returns.
- Using a campfire with a campfire-cooking ingredient places one item in its first free slot. Its four items cook
  independently while lit and drop their results; an unlit fire cools progress by two ticks per tick.
- Clients receive vanilla's seven recipe input property sets and stonecutter choices on join and when recipes
  change. Stonecutter and smithing result slots are still unimplemented.

An identifier (in snake case) names one recipe: registering a taken one throws. Players online follow changes without
reconnecting. Registered recipes are added to their books with vanilla's recipe book packet (without the new-recipe
toast), and an open crafting grid that now crafts something shows it. When the plugin unloads its recipes stop
matching, are removed from the books with vanilla's remove packet, and a grid showing one of their results is worked
out again. A recipe keeps its recipe book id for the life of the process, so a reloaded plugin's recipes come back
under the ids the books had. A player whose client goes back through configuration for the same plugin change (for
new items, say) gets the whole book when they join again instead, as the recipes may show items their client doesn't
know yet. This check also applies during the initial join if content changed during its handshake; the client gets
vanilla recipes first and the rest after reconfiguration. A failed delivery does not stop other players syncing;
the next recipe change retries the affected player. Recipes are made of items, so a vanilla client sees a plugin's
recipes of vanilla items.

### Custom recipe types

Register a server-side machine recipe type during `ConfigureRegistry`. Its identifier is in the plugin's namespace;
the returned `RecipeType<TRecipe>` keeps its numeric identity across reloads. The serializer reads JSON and matches
the machine's input slots:

```csharp
public override void ConfigureRegistry(IPluginRegistry registry)
{
    pressRecipes = registry.RegisterRecipeType("ruby:press", new PressRecipeSerializer());
}

// On the machine's tick, in its block entity, using the plugin's IServer and copies of its input stacks:
var recipe = this.Server?.FindRecipe(pressRecipes, new ItemStack?[] { ingredient });
```

Implement `IRecipeSerializer<TRecipe>.Read(string identifier, JsonElement json)` and
`Matches(TRecipe recipe, IReadOnlyList<ItemStack?> inputs)`. Return immutable recipe values from `Read`; matching
must not change the recipe or inputs. Each file under `data/<namespace>/recipe/<path>.json` names its serializer in
`type`, for example `"type": "ruby:press"`. The file's identifier is `<namespace>:<path>`; the other JSON fields
belong to the serializer. Clone a `JsonElement` you retain. Plugins' files can also use built-in vanilla recipe
types, including cooking, which use the same recipe set as recipes registered in code. Files are read after item
tags; malformed files are logged and left out. Registering an identifier already in use throws.

`IServer.FindRecipe<TRecipe>(RecipeType<TRecipe> type, IReadOnlyList<ItemStack?> inputs)` returns the first matching
recipe, by lexical recipe identifier, or null. `IServer.GetRecipes<TRecipe>(RecipeType<TRecipe> type)` returns all
the type's current recipes in that order, for a machine's screen to list (the
[engine sample](../samples/EngineSample)'s grinder sends them as its menu opens). Look up the current result each tick
rather than retaining recipe values across reloads. Registrations belong to their server and plugin; unloading
removes the serializer and its recipes, and refuses new calls while callbacks already running drain. Plugin recipe
JSON is kept as host-owned data, so rebuilding snapshots never keeps an unloaded serializer alive.

Custom types are server-side processing rules. Vanilla's 1.21.11 `UpdateRecipes` carries fixed input property sets
and stonecutter result displays; it has no field for arbitrary recipe types, serializers or matching. Custom types
are therefore omitted from that packet, do not create a synced client registry and do not require modded clients
on their own. A plugin with a custom screen can send its own display data over a negotiated payload channel, or as
its menu's open data. A mod whose side is `both` registers its recipe types in its shared `ConfigureRegistry`: on
the client, `RegisterRecipeType` gives the type's identity and reads no recipes, which stay the server's.
The server's standalone data packs still do not load recipe files; this API loads plugins' recipe files.

## Brewing

```csharp
public override void ConfigureRegistry(IPluginRegistry registry)
{
    var ruby = registry.RegisterItem("ruby:ruby");

    registry.AddPotionMix(Potion.Awkward, ruby, Potion.Luck);
    registry.AddContainerMix(ItemsRegistry.Potion, ruby, ItemsRegistry.LingeringPotion);
}
```

Brewing stands brew like vanilla's: blaze powder (`#minecraft:brewing_fuel`) fuels 20 brews, a brew takes 400 ticks
and mixes every bottle its ingredient brews, and shift-clicking sends fuel, ingredients and bottles to their slots in
vanilla's order. A potion mix brews a bottle of one potion into the same kind of bottle of another, and a container
mix one kind of bottle (a potion, splash or lingering potion) into another of the same potion, like gunpowder does.
Vanilla's mixes are tried first, then plugins' in the order they were added, so a plugin can't change what vanilla
brews; the result is a new bottle with only its potion, as in vanilla. A plugin's mixes are removed when it unloads
and added again when it reloads; their ingredients may be the plugin's own items. Potions are vanilla's, as plugins
can't add potions. Clients build vanilla's mixes themselves and aren't sent plugins', so a client predicts a click
with a plugin's ingredient as if the slot refused it, and the window the server sends back corrects it.

## Tags

Tags are data pack tag files: `data/<namespace>/tags/block/<path>.json`, `tags/item/...`, `tags/entity_type/...`,
`tags/worldgen/biome/...`, `tags/worldgen/placed_feature/...` or `tags/worldgen/configured_carver/...`, with
`values` (identifiers, `#` followed by a tag, or `{ "id": ..., "required": false }`), `replace`, and NeoForge's
`remove`. A
file for a vanilla tag adds to it, for example `data/minecraft/tags/block/mineable/pickaxe.json` to mine a block with a
pickaxe; other files make new tags, or add to Obsidian's [convention tags](#convention-tags). Obsidian merges vanilla's
tags with the loaded plugins' files in load order, then the server's data packs' files (see
[Data packs and overriding vanilla](#data-packs-and-overriding-vanilla)), whenever a plugin loads or unloads, and
vanilla's `Tag` objects hold the result, so server code using them sees the plugins' entries.
`IPluginRegistry.AddToTag` adds entries from code.

The generator makes a `RubyTags` class with a `TagKey` per tag of the plugin's namespaces:

```csharp
if (RubyTags.Blocks.Ores.Contains(block))
    ...
```

A tag whose required entry is missing keeps its earlier entries rather than disappearing, as server code holds
vanilla's tags. When tags refer to each other through optional references, the reference that would close the cycle
is left out in identifier order, where vanilla's order depends on its hash map. Players already connected get the
changed tags when a plugin loads or unloads, and go back through configuration when its blocks, items, entity types or
channels changed.

Biome tags hold vanilla's and plugins' biomes, so a plugin's biome can join `#minecraft:is_overworld`, and biome
modifiers and level stems can name a plugin's biome tags. Clients are sent the biome tags, vanilla's included, like
vanilla sends them. Placed feature and configured carver tags (vanilla has none) aren't sent: they're for biomes'
`features` and `carvers` and biome modifiers. Tags are applied after plugins' biomes are registered, and may name the
loaded plugins' placed features and carvers.

### Convention tags

Obsidian ships tags in the shared `c` namespace that NeoForge's and Fabric's mods use, filled with vanilla's entries,
so plugins can name and extend common groups without depending on each other: a biome modifier on `#c:is_overworld`,
a check of `#c:ingots/iron`, or a plugin's `data/c/tags/block/ores.json` (and `tags/item/ores.json`) adding its ore
to `#c:ores`. They act as a built-in data pack between vanilla's tags and plugins' files: plugins add to them or
`replace` them, they exist with no plugins loaded, `TagKey`s and `TagsRegistry.TryGet` find them, and clients are sent
them with vanilla's tags. They're standard tag files, kept in `Obsidian/Registries/convention_tags.json` by registry
and path, and refer to vanilla's tags where those hold the set, so a plugin's entry in `#minecraft:iron_ores` is in
`#c:ores/iron` too.

- Blocks and items: `c:ores` and `c:ores/<material>` (coal, copper, diamond, emerald, gold, iron, lapis, redstone,
  quartz, netherite_scrap), `c:ores_in_ground/<stone|deepslate|netherrack>`, `c:storage_blocks/<material>`,
  `c:chests` (`/wooden`, `/trapped`, `/ender`, `/copper`), `c:barrels`, `c:glass_blocks`, `c:glass_panes`, `c:stones`,
  `c:cobblestones`, `c:sands`, `c:gravels`, `c:obsidians`, `c:netherracks`, `c:end_stones`, `c:bookshelves`,
  `c:fences`, `c:fence_gates`, `c:stripped_logs`, `c:stripped_woods`, `c:concretes`, `c:glazed_terracottas`,
  `c:player_workstations/<crafting_tables|furnaces>` and `c:dyed/<color>`; blocks also `c:crops`.
- Items: `c:ingots`, `c:nuggets`, `c:gems`, `c:raw_materials` and `c:dusts`, each with `/<material>`,
  `c:dyes/<color>`, `c:tools` and `c:tools/<kind>` (bow, brush, crossbow, fishing_rod, igniter, mace, shear, shield,
  spear, mining_tool, melee_weapon, ranged_weapon), `c:armors`, `c:foods/...`, `c:crops/...`, `c:seeds/...`,
  `c:rods/...`, `c:buckets/...`, `c:bricks/...`, `c:music_discs`, `c:potions`, and single materials like
  `c:strings`, `c:leathers`, `c:slime_balls` and `c:ender_pearls`.
- Entity types: `c:bosses`, `c:minecarts`, `c:boats`, and `c:capturing_not_supported` and
  `c:teleporting_not_supported`, which hold the bosses.
- Biomes: `c:is_overworld`, `c:is_nether`, `c:is_end` and `c:is_void`; `c:is_hot` (warmer than 0.9, the warm ocean
  and the nether), `c:is_cold` (0.3 or colder, and cold oceans), `c:is_wet` and `c:is_dry` (no rain, the nether and the
  end), each with `/overworld`, `/nether` or `/end` parts; and `c:is_forest`, `c:is_ocean`, `c:is_deep_ocean`,
  `c:is_shallow_ocean`, `c:is_river`, `c:is_beach`, `c:is_mountain` (`/peak`, `/slope`), `c:is_desert`,
  `c:is_plains`, `c:is_savanna`, `c:is_jungle`, `c:is_snowy`, `c:is_taiga`, `c:is_swamp`, `c:is_badlands`,
  `c:is_cave`, `c:is_mushroom` and a few more, like `c:is_windswept` and `c:is_outer_end_island`.

Plugins can't tag fluids, so there are no `c:` fluid tags. Like vanilla's data packs, a convention tag referring to a
vanilla tag that fails fails too.

## Biomes, dimension types and damage types

Like a data pack's, each file is an entry named after its namespace and path:

- `data/<namespace>/worldgen/biome/<path>.json` is the biome `<namespace>:<path>`;
- `data/<namespace>/dimension_type/<path>.json` is a dimension type;
- `data/<namespace>/damage_type/<path>.json` is a damage type.

The files are vanilla 1.21.11's format for these registries. A biome:

```json
{
  "has_precipitation": true,
  "temperature": 0.7,
  "downfall": 0.8,
  "attributes": {
    "minecraft:visual/sky_color": "#78a7ff",
    "minecraft:visual/fog_color": "#c0d8ff"
  },
  "effects": {
    "water_color": "#3f76e4",
    "grass_color": "#6fbf4a",
    "foliage_color": "#59ae30"
  },
  "carvers": ["minecraft:cave", "minecraft:canyon"],
  "features": [],
  "spawners": {
    "creature": [{ "type": "minecraft:sheep", "weight": 12, "minCount": 4, "maxCount": 4 }]
  },
  "spawn_costs": {}
}
```

Obsidian reads the files whenever a plugin loads or unloads, and at startup before the worlds load:

- A plugin's entries get ids after vanilla's, in the order they were first added. Reloading the plugin keeps an
  entry's id and replaces its data.
- Entries stay registered until the server stops, even when their plugin unloads, since chunks and saves refer to
  them. Saved chunks store biomes by name, so after a restart without the plugin its biomes load as plains, as its
  blocks load as air.
- A file for one of vanilla's entries, like `data/minecraft/worldgen/biome/plains.json`, overrides it (see
  [Data packs and overriding vanilla](#data-packs-and-overriding-vanilla)). When files define the same entry, the
  last in order wins, plugins' in load order and then data packs', and the one replaced is logged.
- A file vanilla wouldn't accept is logged and left out, and the plugin loads without it (see below).
- A dimension type keeps its `min_y`, `height` and `logical_height` until the server restarts: a reload changing them
  keeps the entry as it was, with a warning.

Clients are sent a plugin's entries as vanilla's network codecs write the values read from its files, not the files
themselves, and vanilla's entries by name unless a file overrides them, so vanilla clients always decode them like a
data pack's entries. A colour
given as an int is sent as `"#rrggbb"`, defaults vanilla leaves out are left out, and of a biome only its climate,
attributes and effects are sent, as vanilla sends them. Players already connected go back through configuration when
entries are added or their data changes. Plugin biomes generate in plugins' dimensions (see [Dimensions](#dimensions)),
and in vanilla's through biome regions (see [Biome regions](#biome-regions)), and the Obsidian client colours biomes
from compiled tables, so plugin biomes have no tint there yet.

### What's checked

A file is read the way vanilla reads a data pack's entry with the registry's codec, so it's accepted where vanilla
accepts it and left out where vanilla would fail, with a message naming the field and what's expected, like
`effects.water_color: expected a colour: "#rrggbb", an int or a list of 3 floats`:

- Required fields must be there, and optional ones get vanilla's defaults. Fields vanilla doesn't know are ignored,
  as are JSON comments and trailing commas.
- Values must be what vanilla's codecs take: booleans, numbers and strings as JSON values of that kind (`"7"` isn't a
  number), enums by vanilla's names, and identifiers with or without the `minecraft:` namespace. A colour is
  `"#rrggbb"` (`"#aarrggbb"` where vanilla takes alpha), an int, or a list of 3 (or 4) floats, 0 to 1 per channel.
  An int provider, like `monster_spawn_light_level`, is an int or `{"type": ...}` with its type's fields (`constant`,
  `uniform`, `biased_to_bottom`, `clamped`, `clamped_normal`, `weighted_list`).
- Ranges and cross-field rules are vanilla's. A dimension type's `min_y` (-2032 to 2016) and `height` (16 to 4064) are
  multiples of 16, `min_y + height` is at most 2032, `logical_height` is at most `height`, `coordinate_scale` is from
  0.00001 to 30,000,000, and its light levels and providers' values are from 0 to 15. A spawner's `minCount` is at
  most its `maxCount`, and both are positive.
- References must exist. Sound events and particle, entity and activity types are vanilla's (entity types may be a
  loaded plugin's). A sound vanilla doesn't have is given inline as `{"sound_id": ..., "range": ...}`.
- A biome is read whole, like vanilla reads it. Its `features` are vanilla's or loaded plugins' placed features by
  identifier or tag, or placed features defined inline (see [World generation features](#world-generation-features)).
  Its `carvers` are vanilla's or plugins' configured carvers by identifier or tag, or configured carvers defined
  inline in the `worldgen/configured_carver` format.
  Where vanilla would log and drop a carver, feature or spawner it can't read, the file is left out instead.
- A dimension type's `timelines` can only be a timeline tag, like `"#minecraft:in_overworld"`: the server sends clients
  the timeline tags but not the timelines.

In 1.21.11 most of a biome's and dimension type's looks and rules moved into environment `attributes`: sky and fog
colours, particles, music and ambient sounds, and rules like `gameplay/water_evaporates`, `gameplay/piglins_zombify`
or `gameplay/bed_rule`. Older fields such as `effects.sky_color` or a dimension type's `ultrawarm` are unknown and
ignored, as vanilla ignores them. Plugins can set all of vanilla's attributes, as a value or as
`{"modifier": ..., "argument": ...}`, with vanilla's ranges and modifiers; a biome can't set the ones that apply to a
whole dimension (`gameplay/fast_lava` and `gameplay/sky_light_level`). Clients are sent the attributes vanilla syncs.
Particles take their options, like `{"type": "minecraft:dust", "color": [1, 0, 0], "scale": 1}`, checked like
vanilla's particle codecs and sent as they write them; their blocks and items are vanilla's, since clients don't know
plugins' ones, and an item is a name or `{"id": ...}` without components. A bed rule's `error_message` is any text
component vanilla reads, checked for its content and nested components; clients aren't sent bed rules.

The server applies the attributes Obsidian has the mechanics of, like vanilla: a value starts at the attribute's
default, then the dimension type's map applies, then the biome's where it's asked about, so a plugin's biome can
change them in any dimension, and a plugin's dimension type everywhere in its levels (vanilla's own types and biomes
apply their values too):

- `gameplay/monsters_burn` and `gameplay/sky_light_level` (the overworld's day timeline applies after the biome, so
  a biome can't stop monsters burning by day; a biome can't set the sky light level), and `gameplay/fast_lava` (a
  whole dimension's);
- `gameplay/can_start_raid` (a bad omen where the player is starts a raid in a village) and
  `gameplay/can_pillager_patrol_spawn` (where a patrol would spawn, only in the overworld, like vanilla's);
- `gameplay/piglins_zombify` (piglins and hoglins) and `gameplay/snow_golem_melts`;
- `gameplay/bed_rule`: where it `explodes`, using a bed removes it and sets off an explosion of power 5 with fire, as
  in vanilla's nether and end, showing its `error_message` above the hotbar; elsewhere a player who can't sleep now
  is shown it (`when_dark` is when the type's time isn't fixed and the sky is darkened by 4 or more);
- `gameplay/respawn_anchor_works`: glowstone in either hand charges a respawn anchor anywhere, and a charged anchor
  explodes like a bed where it doesn't work (everywhere but vanilla's nether). Like other block explosions, their
  drops follow `block_explosion_drop_decay`;
- `gameplay/water_evaporates`: water poured from a bucket hisses away instead of being placed.

Players can't sleep or set their spawn with a bed or anchor yet, so where those work, using them does nothing more.
The other attributes change what clients show and play.

## World generation features

Plugins add configured and placed features, and change what biomes place and spawn with biome modifiers:

- `data/<namespace>/worldgen/configured_feature/<path>.json` is the configured feature `<namespace>:<path>`;
- `data/<namespace>/worldgen/placed_feature/<path>.json` is a placed feature;
- `data/<namespace>/worldgen/configured_carver/<path>.json` is a configured carver;
- `data/<namespace>/biome_modifier/<path>.json` is a biome modifier.

Features are vanilla 1.21.11's format, read into the same objects as vanilla's features: a configured feature is
`{"type": ..., "config": {...}}` and a placed feature `{"feature": ..., "placement": [...]}`. Every feature, placement
modifier, provider, predicate and placer type vanilla's own features use is available, and `minecraft:no_op`, as
are the types plugins register (see [Feature types](#feature-types)). A
feature refers to vanilla's or plugins' features by identifier, or defines one inline. An ore that replaces stone
with a plugin block, in every overworld biome:

```json
// data/ruby/worldgen/configured_feature/ore_ruby.json
{
  "type": "minecraft:ore",
  "config": {
    "size": 8,
    "discard_chance_on_air_exposure": 0,
    "targets": [
      {
        "target": { "predicate_type": "minecraft:tag_match", "tag": "minecraft:stone_ore_replaceables" },
        "state": { "Name": "ruby:ruby_ore" }
      },
      {
        "target": { "predicate_type": "minecraft:tag_match", "tag": "minecraft:deepslate_ore_replaceables" },
        "state": { "Name": "ruby:deepslate_ruby_ore" }
      }
    ]
  }
}
```

```json
// data/ruby/worldgen/placed_feature/ore_ruby.json
{
  "feature": "ruby:ore_ruby",
  "placement": [
    { "type": "minecraft:count", "count": 6 },
    { "type": "minecraft:in_square" },
    {
      "type": "minecraft:height_range",
      "height": {
        "type": "minecraft:trapezoid",
        "min_inclusive": { "above_bottom": -32 },
        "max_inclusive": { "absolute": 48 }
      }
    },
    { "type": "minecraft:biome" }
  ]
}
```

```json
// data/ruby/biome_modifier/ore_ruby.json
{
  "biomes": "#c:is_overworld",
  "add": { "features": { "underground_ores": "ruby:ore_ruby" } }
}
```

### Biome modifiers

A biome modifier is Obsidian's own format, close to NeoForge's modifiers but one file for every change to a set of
biomes. It has:

- `biomes`, the biomes it changes, and optionally `exclude`, biomes it leaves alone even when in `biomes`;
- `add`, with any of:
  - `features`, placed features to add to the end of each decoration step named, like
    `{ "underground_ores": [...], "vegetal_decoration": "ruby:bush" }`; features added may be defined inline;
  - `spawners`, one `{"type", "weight", "minCount", "maxCount"}` or a list of them, each added to the end of its
    entity type's category's list. As in vanilla, weights are at least 0 and counts at least 1; a modifier that would
    make a list's weights add up to more than 2147483647 is logged and its spawns left out;
  - `carvers`, configured carvers to add to the end of the biomes' carvers, which may be defined inline;
  - `spawn_costs`, `{"<entity type>": {"energy_budget", "charge"}}`, replacing those types' costs in the biomes;
- `remove`, with any of:
  - `features`, placed features to remove, from every step, or from the steps named, as in `add`;
  - `spawners`, entity types whose spawners are removed from every category;
  - `carvers`, configured carvers to remove;
  - `spawn_costs`, entity types whose spawn costs are removed.

Fields the format doesn't have are errors. `biomes`, `exclude`, features, carvers and entity types are sets: an
identifier, a tag like `#c:is_overworld`, or a list mixing both. Biomes, placed features, configured carvers and
their tags may be vanilla's, Obsidian's shared `c:` ones, or plugins'. Entity types are vanilla's or a loaded
plugin's; a vanilla type's category is vanilla's for the type (bees are creatures, guardians monsters), and a plugin
type's is its `Category`. Removing vanilla's swamp bogged or desert parched keeps them removed: they're plain
entries of those biomes' lists. Steps are vanilla's decoration steps: `raw_generation`, `lakes`,
`local_modifications`, `underground_structures`, `surface_structures`, `strongholds`, `underground_ores`,
`underground_decoration`, `fluid_springs`, `vegetal_decoration` and `top_layer_modification`.

A spawn cost, like vanilla's soul sand valley's, spreads a type's mobs out: each mob counting against a cap whose
biome gives its type a cost has a charge, and a mob of a type with a cost only spawns where the charges around, each
divided by its distance and the sum times the new mob's charge, stay within the cost's `energy_budget`. A biome's
`creature_spawn_probability` (0.1 by default, lower in vanilla's snowy and badlands biomes) is the chance of each
further group of creatures spawning when a chunk generates.

A carver and a modifier adding it to a plugin's tag of biomes, and plains spawning the plugin's mob:

```json
// data/ruby/worldgen/configured_carver/crystal_caves.json
{
  "type": "minecraft:cave",
  "config": {
    "probability": 0.1,
    "y": { "type": "minecraft:uniform", "min_inclusive": { "above_bottom": 8 }, "max_inclusive": { "absolute": 60 } },
    "yScale": 0.5,
    "lava_level": { "above_bottom": 8 },
    "replaceable": "#minecraft:overworld_carver_replaceables",
    "horizontal_radius_multiplier": 1,
    "vertical_radius_multiplier": 1,
    "floor_level": -0.7
  }
}
```

```json
// data/ruby/tags/worldgen/biome/has_crystals.json
{ "values": ["#minecraft:is_mountain", "ruby:crystal_peaks"] }
```

```json
// data/ruby/biome_modifier/crystal_caves.json
{ "biomes": "#ruby:has_crystals", "add": { "carvers": "ruby:crystal_caves" } }
```

```json
// data/ruby/biome_modifier/golems.json
{
  "biomes": ["minecraft:plains", "#c:is_savanna"],
  "exclude": "minecraft:sunflower_plains",
  "add": {
    "spawners": { "type": "ruby:golem", "weight": 10, "minCount": 1, "maxCount": 2 },
    "spawn_costs": { "ruby:golem": { "energy_budget": 0.12, "charge": 1.0 } }
  },
  "remove": { "spawners": "minecraft:creeper" }
}
```

A configured carver is vanilla's format with the `minecraft:cave`, `minecraft:nether_cave` or `minecraft:canyon`
type, constant, uniform or trapezoid float providers, constant or uniform heights, and a block tag as `replaceable`.
Like vanilla, each chunk is carved by the chunks within 8 of it, each with the carvers of its biome at Y 0, seeded
by the carver's index in that biome's list. Vanilla's biomes of a dimension all list the same carvers, so worlds
without plugins carve exactly as before.

The rules:

- Files for vanilla's features and carvers override them (see
  [Data packs and overriding vanilla](#data-packs-and-overriding-vanilla)), and when files define the same entry, the
  last in order wins. A file that can't be read is logged and left out, with the features and modifiers that need it;
  the plugin loads without them. Values are checked like the source generator checks vanilla's:
  unknown types, fields, enum names and blocks, and missing required fields, are errors.
- Features may refer to each other in any order and across plugins; a feature that refers back to itself is left
  out. Each feature is read once and shared, so a feature given by identifier is the same instance everywhere.
- A biome's features, carvers, spawners and spawn costs are vanilla's, or a plugin biome's own, with every
  modifier's `add` applied and then every modifier's `remove`, each in load order, so a removal also removes what
  other plugins added. A modifier whose features or carvers can't be resolved is logged and leaves out both, and one
  whose spawners or spawn costs can't be applied leaves out both of those. Features are ordered across a
  dimension's biomes like vanilla orders them, which decides their random seeds, so a world generates like vanilla
  with the same data pack. Only the lists after both phases count: an add that a later removal resolves is fine. When a dimension's final lists would order features in a cycle (one placed both before and after
  another in its biomes, which vanilla can't generate), the error is logged and that dimension's biomes get their own
  features back, without plugins' biome modifiers. Dimensions share biomes, so this repeats until every dimension's
  features can be ordered; other dimensions keep the modifiers where they can.
- Placing a feature made of a plugin's types (its feature type, a placement modifier or a provider inside it) is a
  call into the plugin, like its event handlers, so unloading waits for chunks being decorated with it. Chunks
  decorated while it unloads leave its features out. When placing such a feature throws, the error is logged naming
  the plugins, and the level stops generating new chunks until those plugins reload or unload. The chunk counts as
  decorated without the features left, with a warning (see [Noise types](#noise-types)).
- Features, carvers and spawns aren't sent to clients or saved, so they come and go with their plugin: when plugins
  load or unload, they're read again, every world's generator moves to them for the chunks it carves and decorates
  next, and natural spawning uses the new spawns. Chunks
  already generated keep what was placed. As in vanilla, adding features to a step can change the seeds of the
  features after them in that step.
- Plugin biomes generate in plugins' dimensions, which place their features (see [Dimensions](#dimensions)). A
  plugin's dimension is one more dimension whose features are ordered together: when its final lists would order
  features in a cycle, it's generated without plugins' biome modifiers too. When a plugin change would leave a
  generating dimension's biomes' own features in a cycle, the change's features aren't applied: the previous ones
  stay, and it's logged.

### Feature types

Like a NeoForge mod's `Feature` and `PlacementModifierType`, a plugin can add feature and placement modifier types
that its (and other plugins') feature files name in their `type`. Register them in `ConfigureRegistry`, with
identifiers in the plugin's namespace:

```csharp
public override void ConfigureRegistry(IPluginRegistry registry)
{
    registry.RegisterFeatureType<PillarFeature>("ruby:pillar");
    registry.RegisterPlacementModifierType<AtHeight>("ruby:at_height");
}
```

A feature type extends `ConfiguredFeatureBase`, and a placement modifier type `PlacementModifierBase` (or
`SinglePlacementModifierBase`, which gives one position or none, or `PlacementFilterBase`, which keeps or drops it).
Both are classes with a parameterless constructor, public or not, whose `Type` returns their identifier:

```csharp
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;

// A column of a block up from its origin, through the blocks it may replace.
public sealed class PillarFeature : ConfiguredFeatureBase
{
    public override string Type => "ruby:pillar";

    public required SimpleBlockState State { get; init; }

    public required IIntProvider Height { get; init; }

    public required BlockSet Replaceable { get; init; }

    public override bool Place(FeatureContext context)
    {
        var block = this.State.ToBlock();
        var height = this.Height.Sample(context.Random);
        var placed = false;

        for (var y = 0; y < height; y++)
        {
            var position = context.Origin + new Vector(0, y, 0);
            if (this.Replaceable.Contains(context.Level.GetBlock(position)))
                placed |= context.Level.SetBlock(position, block);
        }

        return placed;
    }
}

// Moves the position to a height.
public sealed class AtHeight : SinglePlacementModifierBase
{
    public override string Type => "ruby:at_height";

    public required int Y { get; init; }

    public override Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position) =>
        new Vector(position.X, this.Y, position.Z);
}
```

```json
// data/ruby/worldgen/configured_feature/pillar.json
{
  "type": "ruby:pillar",
  "config": {
    "state": { "Name": "ruby:ruby_block" },
    "height": { "type": "minecraft:uniform", "min_inclusive": 2, "max_inclusive": 6 },
    "replaceable": ["minecraft:stone", "#minecraft:deepslate_ore_replaceables"]
  }
}
```

```json
// data/ruby/worldgen/placed_feature/pillar.json
{
  "feature": "ruby:pillar",
  "placement": [{ "type": "ruby:at_height", "y": -4 }, { "type": "minecraft:in_square" }]
}
```

A plugin type's JSON is read like vanilla's types: each `snake_case` key sets the property with the same name in
PascalCase (`max_height` sets `MaxHeight`), unknown keys and missing `required` properties are errors, enums match
ignoring case and underscores, and a single value given for a list is a list of it. Properties may be:

- numbers, booleans, strings, enums, lists (`ImmutableArray<T>`, `List<T>`, `IReadOnlyList<T>`, arrays) and
  `Vector`;
- `SimpleBlockState` (`{"Name": ..., "Properties": {...}}`), `BlockSet` (blocks and `#` block tags), `BiomeSet`
  (biomes and `#` biome tags, resolved again whenever plugins' tags change) and `WeightedEntry<T>`
  (`{"data": ..., "weight": ...}`, in lists picked from with `WeightedEntry<T>.Pick` like vanilla's weighted lists);
- vanilla's kinds of worldgen values, which take any type of that kind by its `type`: `IIntProvider` (a number is a
  constant), `IFloatProvider`, `IHeightProvider` and `VerticalAnchor`, `IBlockStateProvider`, `IBlockPredicate` and
  `IRuleTest`; `ConfiguredFeatureBase` and `PlacedFeature`, by identifier or inline; vanilla's tree parts,
  `TrunkPlacer`, `FoliagePlacer`, `RootPlacer`, `TreeDecorator` and `FeatureSize` (see [Tree parts](#tree-parts));
  and `StructureProcessor` (see [Structure processors](#structure-processors));
- the plugin's own classes, read as objects with the same rules.

`RegisterWorldgenType<TBase, TValue>` registers a type of another of those kinds, like
`RegisterWorldgenType<IIntProvider, MyProvider>("ruby:my_provider")`, or of an abstract class or interface of the
plugin's own that its features' properties use. One identifier may name types of different kinds, as
`minecraft:constant` does.

The rules:

- Identifiers are in the plugin's namespace; `minecraft:` ones, and one registered twice for the same kind, are
  errors. Register in `ConfigureRegistry`: the plugin's files are read once it returns, and registering later throws.
- Plugins' types are only found when registered; attributes like `ConfiguredFeatureClass` on them don't count.
- The types are gone when the plugin unloads, and the features made of them are left out when features are read
  again then (logged like any file that can't be read). A reload registers them again.
- Density function, surface rule and biome source types are registered the same way (see
  [Noise types](#noise-types)).

### Tree parts

Vanilla's `minecraft:tree` feature is made of a trunk placer, a foliage placer, an optional root placer, tree
decorators and a feature size (its `minimum_size`). Their base classes are in `Obsidian.API.World.Features.Trees`, so
a plugin can add its own types of each, which vanilla's tree feature and the plugin's own features take, and give its
feature types properties of them:

```csharp
using Obsidian.API.World.Features.Trees;

public override void ConfigureRegistry(IPluginRegistry registry)
{
    registry.RegisterWorldgenType<FoliagePlacer, CubeFoliagePlacer>("ruby:cube");
    registry.RegisterWorldgenType<TreeDecorator, CapDecorator>("ruby:cap");
}

// Square rows of leaves of the placer's radius, for its height, down from the top of the trunk.
public sealed class CubeFoliagePlacer : FoliagePlacer
{
    public required int Height { get; init; }

    public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.Height;

    protected override void CreateFoliage(
        TreeContext tree,
        int freeTreeHeight,
        FoliageAttachment attachment,
        int foliageHeight,
        int foliageRadius,
        int offset)
    {
        for (var y = offset; y > offset - foliageHeight; y--)
            this.PlaceLeavesRow(tree, attachment.Position, foliageRadius, y, attachment.DoubleTrunk);
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random,
        int dx,
        int y,
        int dz,
        int radius,
        bool doubleTrunk) => false;
}

// A block on the tree's highest log.
public sealed class CapDecorator : TreeDecorator
{
    public required SimpleBlockState State { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        if (context.Logs.Count > 0)
            context.SetBlock(context.Logs[^1] + Vector.Up, this.State.ToBlock());
    }
}
```

```json
// data/ruby/worldgen/configured_feature/ruby_tree.json, with vanilla's other tree fields
"foliage_placer": { "type": "ruby:cube", "radius": 2, "offset": 0, "height": 3 },
"decorators": [{ "type": "ruby:cap", "state": { "Name": "ruby:ruby_block" } }]
```

Placers get the tree being placed as a `TreeContext`, with its level, random, providers and setters, which record
each block as part of the tree; the protected helpers (`PlaceLog`, `PlaceLeavesRow`, `TryPlaceLeaf`, `PlaceRoot`,
...) place blocks like vanilla's. A decorator's `TreeDecoratorContext` has the tree's logs, leaves and roots in
vanilla's order. Blocks placed through either are part of the tree, so leaves get their `distance` from its logs
afterwards like vanilla's. Vanilla's `minecraft:fallen_tree` takes the same decorators in its `stump_decorators` and
`log_decorators`; like vanilla's, their context has only the stump's or the log's logs, and no leaves or roots.

A `FeatureSize` gives the radius that must be free around the trunk at each height above its base
(`GetSizeAtHeight`); with `min_clipped_height`, a tree with less room grows shorter, down to that height, instead of
not at all. Vanilla's own placers, decorators and sizes stay in Obsidian.

### Structure processors

Vanilla's `minecraft:fossil` feature runs its templates' blocks through processor lists, `fossil_processors` and
`overlay_processors`, as do template pools' elements (see [Structures](#structures)): lists by identifier, vanilla's
or files', or lists defined inline, as `{"processors": [...]}` or a bare list. The `StructureProcessor` base class is
in `Obsidian.API.World.Structures`, so a plugin can add processor types, which data names by their `processor_type`,
and give its feature types properties of them:

```csharp
using Obsidian.API.World.Structures;

public override void ConfigureRegistry(IPluginRegistry registry) =>
    registry.RegisterWorldgenType<StructureProcessor, DecayProcessor>("ruby:decay");

// Keeps each template block with a chance of its integrity.
public sealed class DecayProcessor : StructureProcessor
{
    public required float Integrity { get; init; }

    public override StructureBlockInfo? ProcessBlock(
        IWorldGenLevel level,
        Vector origin,
        Vector pivot,
        StructureBlockInfo original,
        StructureBlockInfo current,
        TemplatePlacement placement) =>
        placement.GetRandom(current.Position).NextFloat() < this.Integrity ? current : null;
}
```

```json
// data/ruby/worldgen/configured_feature/ruby_fossil.json, with vanilla's other fossil fields
"fossil_processors": [{ "processor_type": "ruby:decay", "integrity": 0.8 }],
"overlay_processors": "minecraft:fossil_coal"
```

`ProcessBlock` gets each block at its world position and returns the block to place, or `null` to drop it;
`FinalizeProcessing` then sees every kept block at once. A processor whose result depends on the template's other
blocks overrides `ProcessesWholeTemplate`, so the whole template is processed even where only part of it is placed.
`TemplatePlacement.GetRandom` is vanilla's placement random: the placement's own, or one seeded from the position.
Processors are shared between threads, so keep them stateless. Vanilla's own processors stay in Obsidian.

A `data/<namespace>/worldgen/processor_list/<path>.json` file is a processor list, `{"processors": [...]}` or a bare
list, which fossils and template pools name by identifier, like vanilla's (see [Structures](#structures)).

## Structures

Plugins add structures like a data pack, in vanilla 1.21.11's formats:

- `data/<namespace>/worldgen/structure/<path>.json` is the structure `<namespace>:<path>`;
- `data/<namespace>/worldgen/structure_set/<path>.json` is a structure set, the structures that share a placement;
- `data/<namespace>/worldgen/template_pool/<path>.json` is a jigsaw template pool;
- `data/<namespace>/worldgen/processor_list/<path>.json` is a processor list (see
  [Structure processors](#structure-processors));
- `data/<namespace>/structure/<path>.nbt` is the structure template `<namespace>:<path>`, gzipped NBT as a structure
  block saves it.

A tower of one piece, in the biomes of a tag of the plugin's:

```json
// data/ruby/worldgen/structure/tower.json
{
  "type": "minecraft:jigsaw",
  "biomes": "#ruby:has_structure/tower",
  "spawn_overrides": {},
  "step": "surface_structures",
  "terrain_adaptation": "beard_thin",
  "start_pool": "ruby:tower/start",
  "size": 1,
  "start_height": { "absolute": 0 },
  "project_start_to_heightmap": "WORLD_SURFACE_WG",
  "max_distance_from_center": 80,
  "use_expansion_hack": false
}
```

```json
// data/ruby/worldgen/template_pool/tower/start.json
{
  "fallback": "minecraft:empty",
  "elements": [
    {
      "weight": 1,
      "element": {
        "element_type": "minecraft:single_pool_element",
        "location": "ruby:tower/base",
        "processors": "minecraft:empty",
        "projection": "rigid"
      }
    }
  ]
}
```

```json
// data/ruby/worldgen/structure_set/towers.json
{
  "structures": [{ "structure": "ruby:tower", "weight": 1 }],
  "placement": { "type": "minecraft:random_spread", "spacing": 32, "separation": 8, "salt": 1748312 }
}
```

```json
// data/ruby/tags/worldgen/biome/has_structure/tower.json
{ "values": ["#minecraft:is_forest", "ruby:crystal_plains"] }
```

with the template saved by a structure block in `data/ruby/structure/tower/base.nbt`. Its jigsaw blocks name the pools
the tower grows from, up to its `size`.

- Structures are any of vanilla's data-driven types: `minecraft:jigsaw`, and the single-piece and generated ones,
  whose fields are the same as in vanilla's own structure files (`minecraft:desert_pyramid`, `minecraft:igloo`,
  `minecraft:shipwreck`, `minecraft:ocean_ruin`, `minecraft:ruined_portal`, `minecraft:mineshaft`, and so on). A jigsaw
  structure's `max_distance_from_center` is a number from 1 to 128 or `{"horizontal": 1 to 128, "vertical": 1 to
  4064}` (4064 by default), and its `dimension_padding` a number or `{"bottom": ..., "top": ...}` (0 by default, and
  for values that aren't valid, as vanilla reads them leniently), like vanilla's. As in vanilla, only an omitted
  `dimension_padding` lets the start piece reach outside the world's height: an explicit one, even `0` or `{}`, keeps
  it inside. Plugins add structure and pool element types of their own in code (see
  [Structure and pool element types](#structure-and-pool-element-types)).
- Structure sets take `minecraft:random_spread` (with `spread_type`) and `minecraft:concentric_rings` placements, with
  `salt`, `frequency`, `frequency_reduction_method`, `locate_offset` and an `exclusion_zone` naming another set. A set's
  structures are named by identifier.
- Pools take `minecraft:single_pool_element`, `minecraft:legacy_single_pool_element`, `minecraft:list_pool_element`,
  `minecraft:feature_pool_element` (a placed feature, a plugin's too), `minecraft:empty_pool_element` and plugins'
  element types, with `rigid` or `terrain_matching` projections, and a `fallback` pool.
- Values are checked like vanilla's codecs check them (a jigsaw's `size` from 0 to 20, a pool element's `weight` from
  1 to 150, a `spacing` larger than its `separation`, ...). Entries refer to each other by identifier, vanilla's or
  any plugin's or data pack's; a file that can't be read is logged and left out, with the entries that need it.
- Templates are read like a data pack's files, within its limits: at most 32 MiB each and 512 MiB together with the
  pack's other files, once decompressed. A template whose lists claim more than its data holds, that's nested deeper
  than vanilla's NBT allows (512), or that would keep more than about four million entries (list elements, compound
  fields, arrays at one entry per 16 bytes, and each palette's copy of the blocks and their data) is left out. A
  template a pool names that no plugin, pack or vanilla has is empty, as in vanilla.

Structures generate in every dimension whose biomes their `biomes` hold, vanilla's and plugins' (biome regions'
biomes included), placed by their sets like vanilla's. All structures, vanilla's and plugins', are ordered like a
vanilla data pack's registry, by path then namespace, which decides their seeds within their decoration step. A start
is saved in its start chunk by its structure's identifier, with what its pieces placed, and found again from the
seed.

The rules:

- Structures come and go with their plugin, like features: each plugin change reads them again, and the chunks not
  generated yet get the new structure sets' structures, at starts not found yet, while chunks generated before stay
  as they are. A structure partly generated when its plugin unloads isn't finished; its start's state stays saved,
  and the rest of it is placed in the chunks generated once the plugin is back. Unloading doesn't wait for a piece
  being placed: the start's state is kept once it's placed, and the plugin is released after that.
- After a restart without the plugin, its starts load and stay saved as they were, and aren't placed.
- Starting and placing a structure made of a plugin's types (a processor, a height provider or a feature type, in
  the structure, its pools or the processor lists they name) is a call into the plugin, like placing its features.
  One whose plugin unloads isn't started or placed, and its failing stops the dimension like a failing feature (see
  [Noise types](#noise-types)). A jigsaw's pieces name pools by identifier, so every jigsaw structure counts the
  plugins of every pool read from files. A start keeps the plugins it started with: once one unloads, the start isn't
  placed any more, even if its structure set stays (a vanilla set whose overridden template reached the plugin's
  pool, say), and the set starts again without the plugin in chunks not generated yet, with the state the start had.
- Structure tags, `data/<namespace>/tags/worldgen/structure/<path>.json`, merge with vanilla's like other tag
  files and may name plugins' and data packs' structures. Explorer maps and dolphins (`minecraft:dolphin_located`)
  find structures through them, so a plugin's structure added to `#minecraft:on_treasure_maps`, say, is found by
  treasure maps. A tag file naming a structure that doesn't exist fails, and the tag keeps vanilla's entries.
- `/locate structure <structure|#tag>` (permission level 2, like vanilla's) finds the nearest structure of a type or
  tag, vanilla's or a plugin's, in the sender's dimension, searching like vanilla's: 100 rings of placement regions
  for spread placements, nearest first, and every ring position for concentric rings. It answers with vanilla's
  messages and a clickable position that suggests `/tp`. Eyes of ender fly toward the nearest structure of
  `#minecraft:eye_of_ender_located`, searched the same way, so a plugin's structure added to that tag is found by
  them; with none, or when the player aims at an end portal frame within reach, using an eye does nothing and keeps
  it. Like vanilla's, a thrown eye drops as an item four times in five and breaks otherwise, and is saved with its
  item but not where it heads, so one loaded again flies on as it moved and breaks. Obsidian has no `/locate biome`
  or `/locate poi`. Successful throws count toward the eye's `minecraft:used` statistic.

### Structure and pool element types

Like a NeoForge mod's structure types and pool element types, a plugin can add structure logic and jigsaw pieces in
code, which its (and other plugins') files name in their `type` and `element_type`. Register them in
`ConfigureRegistry`, with identifiers in the plugin's namespace:

```csharp
using Obsidian.API.World.Generator;
using Obsidian.API.World.Structures;

public override void ConfigureRegistry(IPluginRegistry registry)
{
    registry.RegisterWorldgenType<CustomStructure, CrystalSpire>("ruby:crystal_spire");
    registry.RegisterWorldgenType<CustomPoolElement, CrystalCluster>("ruby:crystal_cluster");
}

// A spire of ruby blocks in the middle of its chunk, as tall as its height provider says.
public sealed class CrystalSpire : CustomStructure
{
    public required IIntProvider Height { get; init; }

    public override Vector? FindStart(StructureStartContext context)
    {
        var x = (context.ChunkX << 4) + 8;
        var z = (context.ChunkZ << 4) + 8;
        return new Vector(x, context.GetBaseHeight(x, z, HeightmapType.WorldSurfaceWG), z);
    }

    public override void AddPieces(
        StructureStartContext context,
        Vector start,
        ICollection<CustomStructurePiece> pieces)
    {
        var height = this.Height.Sample(context.Random);
        pieces.Add(new SpirePiece(new BlockArea(start, start + new Vector(0, height - 1, 0))));
    }
}

public sealed class SpirePiece(BlockArea bounds) : CustomStructurePiece(bounds)
{
    public override void Place(CustomPieceContext context)
    {
        var block = new SimpleBlockState { Name = "ruby:ruby_block" }.ToBlock();
        for (var y = 0; y <= this.Bounds.Max.Y - this.Bounds.Min.Y; y++)
            context.SetBlock(new Vector(0, y, 0), block);
    }
}

// A 3 by 3 crystal cluster, hanging off any jigsaw that targets minecraft:bottom, like vanilla's feature elements.
public sealed class CrystalCluster : CustomPoolElement
{
    public override Vector Size => new(3, 2, 3);

    public override IReadOnlyList<PoolJigsaw> GetJigsaws() =>
        [new(new Vector(1, 0, 1), "down_south", "minecraft:bottom")];

    public override bool Place(CustomPieceContext context)
    {
        var block = new SimpleBlockState { Name = "minecraft:amethyst_block" }.ToBlock();
        context.SetBlock(new Vector(1, 0, 1), block);
        context.SetBlock(new Vector(1, 1, 1), block);
        context.SetBlock(new Vector(0, 0, 1), block);
        return true;
    }
}
```

```json
// data/ruby/worldgen/structure/crystal_spire.json
{
  "type": "ruby:crystal_spire",
  "biomes": "#ruby:has_structure/crystal_spire",
  "spawn_overrides": {},
  "step": "surface_structures",
  "terrain_adaptation": "beard_thin",
  "height": { "type": "minecraft:uniform", "min_inclusive": 6, "max_inclusive": 12 }
}
```

```json
// an element of a template pool
{ "weight": 1, "element": { "element_type": "ruby:crystal_cluster", "projection": "rigid" } }
```

- Obsidian reads what every structure has, `biomes`, `spawn_overrides`, `step` and `terrain_adaptation`, and every
  pool element's `projection` (`rigid` when it's left out); the file's other keys set the type's properties, read
  like a [feature type's](#feature-types). Obsidian checks the structure's `biomes` at the start `FindStart` gives,
  adapts the terrain around its pieces, and places them: each chunk a piece's `Bounds` reach places its own part, the
  context's `Box`. `CustomPieceContext.SetBlock` places at an offset from the piece's lowest corner, or from a pool
  element's origin turned with the element (its block turned too, a plugin's by its
  [properties](#blocks-in-code)), and only inside `Box`; `Level` reaches the world.
- Starts aren't saved with their pieces: like Obsidian's own structures, they're made again from the seed when a chunk
  needs them, also after a restart, and only each piece's box is saved. So `FindStart`, `AddPieces` and `Place` must
  depend only on their context (its random is seeded for the chunk). Types are shared between threads; keep them
  stateless.
- A pool element's `Size` is the blocks it may place from its origin, which jigsaw assembly keeps apart from other
  pieces, and its jigsaws (`PoolJigsaw`, like the jigsaw blocks a structure block saves: a position, the
  `orientation`, `name`, `target`, `pool` and joint) connect it; by default it has one, facing down at its origin,
  named `minecraft:bottom`.
- The types come and go with their plugin, like feature types: a structure or pool element naming a type that isn't
  registered is left out, logged like any file that can't be read (`ruby:crystal_spire isn't a structure type`), with
  the entries that need it (the sets placing the structure, the structures starting from the pool). Starting and
  placing a structure made of them is a call into the plugin, and its failing stops the dimension like a failing
  feature. A start made with the plugin isn't placed once it unloads, and keeps its saved state; after a restart
  without the plugin, its starts load and stay as they were saved, rather than being dropped as vanilla drops starts
  of unknown structures, so the structure carries on when the plugin is back.


## Noise settings, density functions and noises

Plugins add the data a dimension's terrain is made of, like a data pack:

- `data/<namespace>/worldgen/noise_settings/<path>.json` is the noise settings `<namespace>:<path>`;
- `data/<namespace>/worldgen/density_function/<path>.json` is a density function;
- `data/<namespace>/worldgen/noise/<path>.json` is a noise, `{"firstOctave": ..., "amplitudes": [...]}`;
- `data/<namespace>/worldgen/multi_noise_biome_source_parameter_list/<path>.json` is a multi-noise parameter list,
  which in vanilla 1.21.11 only names a built-in preset: `{"preset": "minecraft:overworld"}` or `minecraft:nether`.

They're vanilla 1.21.11's formats, read into the same objects as vanilla's own (a parity test reads all of vanilla's
files at runtime, compares them with the generated ones, and generates vanilla's chunks with them). Noise settings
have every field vanilla's have: `sea_level`, `default_block`, `default_fluid`, `noise` (the cells' `min_y`, `height`,
`size_horizontal`, `size_vertical`, checked like vanilla), the 15 functions of the `noise_router`, `spawn_target`,
the flags and `surface_rule`, whose rules and conditions are inline as in vanilla. A density function is a number (a
constant), an identifier, or an object with a `type`; a noise is an identifier or defined inline (see below). A spline
function's `spline` is a multipoint spline or a number, for a constant one, and a spawn target's parameters, its
`depth` included, are a number or a range, as in a multi-noise biome source. Plugins' dimensions use plugins' noise settings (see [Dimensions](#dimensions)). Ground rolling
around y 64 with a plugin's noise:

```json
// data/ruby/worldgen/noise/bumps.json
{ "firstOctave": -4, "amplitudes": [1.0, 0.5] }
```

```json
// data/ruby/worldgen/density_function/ground.json
{
  "type": "minecraft:add",
  "argument1": { "type": "minecraft:y_clamped_gradient", "from_y": 0, "to_y": 128, "from_value": 1, "to_value": -1 },
  "argument2": { "type": "minecraft:noise", "noise": "ruby:bumps", "xz_scale": 0.25, "y_scale": 0 }
}
```

The noise settings then name `"final_density": "ruby:ground"` in their `noise_router`.

The rules:

- Files for vanilla's entries override them (see
  [Data packs and overriding vanilla](#data-packs-and-overriding-vanilla)), and when files define the same entry, the
  last in order wins. A file that can't be read is logged and left out, with the entries that need it; the plugin
  loads without them. Unknown types, fields and blocks, missing fields and values out of vanilla's ranges are
  errors, such as a `find_top_surface` whose `cell_height` isn't positive or an `old_blended_noise` scale out of
  0.001 to 1000.
- Entries refer to vanilla's or plugins' entries by identifier, in any order and across plugins; one that refers
  back to itself is left out. Each is read once and shared, like vanilla's registry entries, so a density function
  named twice is one function, whose caches are shared.
- A dimension keeps the noise settings it was created with until the server stops: a reload that changes them, or
  the density functions and noises they use, keeps the dimension as it was, with a warning, and the change applies
  after a restart.
- Vanilla seeds a noise from its identifier, so its codec reads a noise defined inline, like
  `"noise": { "firstOctave": -4, "amplitudes": [1, 0.5] }`, but generating with it fails. Obsidian seeds an inline
  noise from its parameters instead: inline noises with the same `firstOctave` and `amplitudes` are the same noise,
  like noises of one identifier, so name a noise file to have two different noises of the same parameters. Noise
  settings may be defined inline in a level stem (see [Dimensions](#dimensions)).

### Noise types

Like a NeoForge mod's density function, surface rule and biome source types, a plugin registers types that its
(and other plugins') files name in their `type`, with `RegisterWorldgenType` in `ConfigureRegistry`:

```csharp
registry.RegisterWorldgenType<IDensityFunction, Terraces>("ruby:terraces");
registry.RegisterWorldgenType<ISurfaceRule, Crust>("ruby:crust");
registry.RegisterWorldgenType<ISurfaceCondition, BelowY>("ruby:below_y");
registry.RegisterWorldgenType<BiomeSourceBase, Rings>("ruby:rings");
```

They're read like [feature types](#feature-types), with the same rules, and:

- A density function implements `IDensityFunction`: `GetValue` and the bounds of its values, `MinValue` and
  `MaxValue`. One with density functions, noises or splines in its properties, or in lists of them at any depth,
  implements `MapAll` too, rebuilding itself with each passed through the visitor (`visitor.Map(...)`,
  `visitor.VisitNoise(...)`), as vanilla's do: that's how generation seeds its noises and gives it the chunk's caches.
  Registering one that doesn't is an error.
  Plugins' functions are sampled block by block, where vanilla's are filled a cell at a time.
- A surface rule implements `ICustomSurfaceRule`, whose `Apply` gives the block for the context's block, or null to
  let the next rule of a sequence try; a condition implements `ICustomSurfaceCondition`, whose `Test` says whether it
  holds. The context has the block's position, biome, surface and stone depths, water height and steepness, and
  evaluates other rules and conditions, such as the type's own properties. Vanilla's `sequence`, `condition` and
  `not` combine them with vanilla's rules.
- A biome source extends `BiomeSourceBase`: `PossibleBiomes` lists the biomes it gives (read once, when the level
  stem is) and `GetBiome` picks one per quart from the climate sampler's raw values.
- All of them run for many positions of every chunk, on several threads at once: keep them fast and stateless.

```csharp
// Steps a function's values into terraces.
public sealed class Terraces : IDensityFunction
{
    public string Type => "ruby:terraces";

    public required IDensityFunction Argument { get; init; }

    public required double Step { get; init; }

    public double MinValue => Math.Floor(this.Argument.MinValue / this.Step) * this.Step;

    public double MaxValue => Math.Floor(this.Argument.MaxValue / this.Step) * this.Step;

    public double GetValue(double x, double y, double z) =>
        Math.Floor(this.Argument.GetValue(x, y, z) / this.Step) * this.Step;

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new Terraces
    {
        Argument = visitor.Map(this.Argument),
        Step = this.Step
    });
}

// Puts a block on the top block of the default block (usually stone).
public sealed class Crust : ICustomSurfaceRule
{
    public string Type => "ruby:crust";

    public required SimpleBlockState Block { get; init; }

    public IBlock? Apply(ISurfaceContext context) => context.StoneDepthAbove <= 1 ? this.Block.ToBlock() : null;
}
```

```json
{ "type": "ruby:terraces", "argument": "ruby:ground", "step": 0.25 }
```

```json
{ "type": "ruby:crust", "block": { "Name": "ruby:ruby_block" } }
```

Generating with a plugin's types is a call into the plugin, like placing its features: each step of a chunk (biomes,
noise, surface, carvers, decoration) holds the plugins whose types the dimension's noise settings and biome source are
made of, so unloading waits for chunks being generated. When such a plugin unloads, the dimension's level stops
generating new chunks, with a warning, and lets go of the plugin's code: chunks already generated still load and are
saved. When the plugin loads again (a reload, or a later load), the dimension is read again from the level stem it was
created with, with the plugin's new types, and the level generates again from its next chunk; chunks generated before
stay as they are. A dimension whose stem, or the plugin data it reads (the noise files of its noise settings and
biome source, and the biomes it names, through tags too), changed since it was added stays stopped until a restart,
with a warning, since generating it differently would mix two generators' chunks. A dimension made of
vanilla's types alone, with plugins' data, keeps generating. A level stem naming another plugin's density functions or
noises, in its noise settings or biome source, generates with that plugin's types too, and resumes when that plugin
reloads, without reloading the stem's own plugin.

A plugin's code failing while a dimension generates with its types (an exception from a density function, surface
rule or biome source) is logged as an error naming the plugins, and the level stops generating new chunks, as when
the plugin unloads, rather than stopping chunk management or the server. It generates again once those plugins
reloaded (or unloaded, when only their features failed), or after a restart. Such a dimension's levels
stop on any generation failure, since plugins' density functions run all through the noise and the failure can't be
told apart from Obsidian's. Failures generating with vanilla's types alone still stop the server.

A step that fails partway leaves nothing for the level to build on when it generates again:

- A step before decoration (biomes, noise, surface, carvers) only writes into its chunk, but reads what it wrote and
  marks blocks for post-processing, so running it again over its partial result could differ. The chunk goes back
  to before its biomes, and those steps run again from scratch.
- Decoration writes into the chunk's neighbors and adds entities and scheduled ticks, which placing its features
  again would duplicate. The chunk counts as decorated, with a warning: it keeps the features placed before the
  failure and lacks the rest. Later steps (post-processing, light) don't run plugins' features, and run once the
  level generates again.
- Structure pieces placed until the failure are kept, and saved, with their start, so generating again doesn't place
  their entities again.

## Dimensions

A plugin adds a dimension with a level stem file, like a data pack: `data/<namespace>/dimension/<path>.json` is the
dimension `<namespace>:<path>`. The server's default world gets a level of that name, generated by the generator the
stem names: vanilla's noise, flat or debug generator, or a plugin's level generator. A dimension with one plugin
biome everywhere:

```json
// data/ruby/dimension/crystal_world.json
{
  "type": "minecraft:overworld",
  "generator": {
    "type": "minecraft:noise",
    "settings": "minecraft:overworld",
    "biome_source": { "type": "minecraft:fixed", "biome": "ruby:crystal_caves" }
  }
}
```

The file is vanilla 1.21.11's level stem, read with vanilla's rules (see [What's checked](#whats-checked)):

- `type` names a dimension type, vanilla's or a plugin's, or defines one inline (see below). It decides the level's
  build range, sky light and everything else a dimension type does, and clients are sent it like any dimension type.
- `generator` is `minecraft:noise`, `minecraft:flat`, `minecraft:debug` or a plugin's level generator (see below).
  `minecraft:noise` has `settings` naming vanilla's noise settings (`minecraft:overworld`,
  `amplified`, `large_biomes`, `caves`, `floating_islands`, `nether` or `end`) or a plugin's (see
  [Noise settings](#noise-settings-density-functions-and-noises)), the plugin's own or a plugin loaded before it, or
  defining them inline, and a `biome_source`, one of vanilla's or a type a plugin registered (see
  [Noise types](#noise-types)):
  - `minecraft:multi_noise`, with an inline `biomes` list of `{"biome": ..., "parameters": {...}}` or a `preset`
    naming a parameter list (vanilla's `minecraft:overworld` or `minecraft:nether`, or a
    `worldgen/multi_noise_biome_source_parameter_list` file's), or inline as `{"preset": "minecraft:overworld"}`. Each
    parameter (`temperature`, `humidity`, `continentalness`, `erosion`, `depth`, `weirdness`) is a number from -2 to 2
    or a range, `[min, max]` or `{"min": ..., "max": ...}`, and `offset` is from 0 to 1. Biomes are picked from the
    climate like vanilla picks them;
  - `minecraft:fixed`, with one `biome`;
  - `minecraft:checkerboard`, with `biomes` (a biome, a list of them or a biome tag, not empty) and a `scale` from 0
    to 62 (2 when missing or invalid, like vanilla);
  - `minecraft:the_end`, the end's islands and biomes.
- Noise settings defined inline are read like a plugin's noise settings file (see
  [Noise settings](#noise-settings-density-functions-and-noises)): their density functions may be inline or name
  vanilla's and plugins' density functions and noises, and their types may be plugins'. The dimension carves like
  the overworld with them. A reload changing them changes the stem, so it applies after a restart like any change
  to the stem, as does one changing the plugin entries they name. A plugin biome source's density functions may be
  inline the same way.
- Biomes are vanilla's or plugins', named or defined inline.
- A dimension type or biome defined inline, as vanilla's stem allows, becomes an entry of the plugin's or data pack's
  like a file's, named for the stem, since clients are sent them by name: the stem `ruby:crystal_world`'s type is
  `ruby:crystal_world/type`, and its biomes `ruby:crystal_world/biome_0`, `ruby:crystal_world/biome_1` and so on, in
  the order they're found (the fixed source's `biome`, the checkerboard's or the multi-noise list's `biomes` in their
  order, or the flat generator's `biome`). They get ids, are sent to clients and come and go with their plugin like
  files' entries, and a reload changing one changes the stem too. A file of the same identifier competes with the
  inline entry like two files (the last wins). An inline entry is registered even when the stem it's in can't be
  read.
- `minecraft:flat` has vanilla's flat `settings`: `layers`, a list of `{"block": ..., "height": ...}` filling the
  level from its bottom up (a missing or unknown block is air and a height is from 0 to 4064, adding up to at most
  4064, as in vanilla; layers above the build range are cut), and a `biome` everywhere (plains when missing or
  unknown, as in vanilla). Players are placed on top of the layers at the center. Lakes, features and structures
  aren't generated in flat dimensions yet, so `"lakes": true`, `"features": true` and any `structure_overrides` but
  `[]` (when it's missing, vanilla places every structure set) are rejected, saying so.
- `minecraft:debug` is vanilla's debug world: a barrier floor at y 60 and every block state at y 70, each in a cell
  of 2 by 2 blocks laid out like vanilla's, so a state is where vanilla players expect it, in plains. Plugins' blocks
  follow vanilla's, as their state ids do; blocks registered after the level started join the grid after a restart.
  Players are placed at y 80 above the grid's corner, like vanilla's.
- Any other `type` names a loaded plugin's level generator (see [Level generators](#level-generators)), which is
  Obsidian's extension of the stem; the generator object's other fields are its settings, like
  `{"type": "ruby:sky_islands", "island_size": 12}`. A generator implementing `IConfigurableLevelGenerator` gets the
  whole object in `Configure` before each new instance's `Init`; others ignore it. The level stops when the plugin
  unloads and resumes, configured again, when it loads, like a world generating with it. A reload changing the stem
  keeps the level stopped until a restart, logged, rather than configuring the new instance with the settings it took.
- Not supported yet, and rejected with a message saying so: noise settings that generate outside the dimension
  type's build range (vanilla clamps them; its own pairings all fit). Generation fills whole cells of
  `size_vertical * 4` blocks from the cell holding `min_y`, so a `min_y` that isn't a multiple of that fills from
  below it: the overworld's `min_y` -64 with `size_vertical` 3 fills from -72, outside the overworld's build range.

A noise dimension generates like a vanilla dimension with those settings: terrain, surface, aquifers and ore veins
come from the noise settings, caves and canyons are carved like in the vanilla dimension using the same noise settings
(like the overworld with a plugin's noise settings), features are the dimension's biomes' (see
[World generation features](#world-generation-features)), and structures generate where their biomes allow, as in
vanilla. Players are placed on the ground near the climate's spawn, like the overworld's spawn.

The rules:

- A file for one of vanilla's dimensions overrides how it generates (see
  [Data packs and overriding vanilla](#data-packs-and-overriding-vanilla)); the rest of this section is about new
  dimensions. When files define the same dimension, the last in order wins. A file that can't be read is logged and
  left out; the plugin loads without it. At startup, what a file names may come with a later plugin or data pack, or
  change as vanilla's dimension types settle (see
  [Data packs and overriding vanilla](#data-packs-and-overriding-vanilla)), so it's read again once startup has read
  every plugin and pack, and only logged and left out if it still can't be. Until its level is created, a dimension
  follows the files: its stem is read again whenever plugins or data packs change, so at startup it sees the noise
  settings, parameter lists and types later plugins and data packs override, and when the file winning goes, the one
  before it wins again. A dimension whose biomes' features can't be ordered (vanilla can't generate it either) is
  logged and left out; as its level was never created, it keeps following the files, so a reload correcting its stem
  applies without a restart.
- A file's path gives the dimension's identifier, and, as vanilla checks resource paths, one with an empty, `.` or
  `..` segment (like `data/ruby/dimension//world.json`) is refused. This holds for every plugin data file.
- A dimension's level is created when its plugin loads, at startup or later, and is saved in the world's folder under
  `dimensions/<namespace>/<path>`, where vanilla saves a data pack's dimensions, and loaded from there after a restart.
  Players can reach it, and it ticks, once it's loaded or generated with its spawn. One that fails to load or generate
  is logged and left out, and the next plugin load tries it again. A plugin loading while the worlds load gets its
  dimensions' levels too, created after those of the plugins loaded before.
- Gameplay follows the dimension type, as in vanilla: monsters spawn with its monster light limits, and undead mobs
  burn by day in a type with the overworld's timelines (`#minecraft:in_overworld`) or its own
  `minecraft:gameplay/monsters_burn`. Raids start where its type's `minecraft:gameplay/can_start_raid` allows, as
  in vanilla, which defaults to true (vanilla's nether sets it false). Patrols, wandering traders and phantom spawns
  stay in the overworld, where vanilla runs them. Like vanilla, which only fights the dragon in the level
  `minecraft:the_end` of the end's dimension type (and saves one fight per world in level.dat), a plugin's
  dimension has no end fight, even with the end's type, noise settings and biome source. There are no portals to
  plugins' dimensions.
- A dimension's level stays until the server stops, even when its plugin unloads, with the noise settings it was
  created with. Its features come and go with their plugins like any features.
- A reload changing a level stem, or the plugin data it reads (noise settings, density functions and noises, its
  biome source's included, and the biomes it names), keeps the dimension as it was, with a warning: the change
  applies after a restart, since changing a live level's generator would mix two generators' chunks. If the
  dimension stops meanwhile, it stays stopped until then.
- A dimension generating with a plugin's types, or with a plugin's level generator, stops generating new chunks when
  that plugin unloads, and generates again when it loads again (see [Noise types](#noise-types) and
  [Level generators](#level-generators)); its generated chunks still load. A stem naming a plugin's generator is only
  read while the plugin is loaded: until a level takes the dimension, it's left out, logged, while the plugin is
  away.
- After a restart without the plugin, its dimension isn't loaded, and its folder is left alone. Players who were in it
  join at the world's spawn.

Players get there with `/dimension <id>`, like `/dimension ruby:crystal_world`, and plugins with
`IPlayer.TeleportToDimensionAsync("ruby:crystal_world")`, which sends a player to the dimension's spawn. Vanilla
clients see a plugin's dimension as a level of that name with its dimension type, sent in login and respawn with the
build height of the type, and its plugin biomes in chunks by their registry ids. Plugins can register portals to
these levels (see [Travel and portals](#travel-and-portals)); data-defined plugin dimensions belong to the default
world.

## Data packs and overriding vanilla

Like vanilla's data packs, data can replace vanilla's entries, so a pack can change vanilla's world generation: a
Terralith-style pack overriding the overworld's level stem, noise settings, parameter list and biomes generates its
overworld instead of vanilla's. The data comes from two places:

- The server's `datapacks` folder, next to `plugins` (`Paths:DataPacks` sets another, like `Paths:Plugins`). Each
  folder or zip file in it with a `pack.mcmeta` is a vanilla data pack, read at startup in name order, after the
  plugins, so the operator's packs win. Obsidian reads the same files of a pack as of a plugin's `data/` folder:
  biomes, dimension and damage types, features, carvers, processor lists, structures, structure sets, template pools,
  structure templates, noise settings, density functions, noises, parameter lists, level stems and tags (blocks,
  items, entity types, biomes, placed features and configured carvers), and loot tables. Other files, like recipes, are
  left out unread, and how many is logged. Like vanilla, a pack's `overlays` whose formats include vanilla
  1.21.11's data pack format (94.1) are applied over its root, the last listed winning, with vanilla's checks of
  `min_format`, `max_format` and `formats` (an overlays section that fails them is logged and not applied), and its
  `filter` blocks the files of the packs before it whose namespace and path match its patterns (vanilla's rule: any
  pattern's `namespace` and any pattern's `path`, matched anywhere in the text). Patterns are Java regular
  expressions, with `\Q...\E` quoting and possessive quantifiers; Java-only constructs .NET lacks otherwise, like
  POSIX classes (`\p{Lower}`), make the filter fail to read, which is logged and leaves it out. As in vanilla, a pack
  needs a `pack` section with a valid text component `description`, or it isn't loaded at all; formats
  (`pack_format`, `min_format`, `max_format`, `supported_formats`) that fail vanilla's checks are logged, and the
  pack is still loaded, as vanilla's server loads it. Plugins' files and vanilla's own entries aren't filtered. Data
  packs are read once: a change applies after a restart.
  Unlike vanilla, a pack has limits, so that it can't exhaust the server's memory: at most 65,536 files, and at most
  32 MiB per file and 512 MiB in all for the files Obsidian reads, decompressed. A pack over them, or a zip file with
  a path leaving the pack (`..` or `\`), is logged and left out; symbolic links in a pack folder are skipped.
- Plugins' own `data/minecraft/...` files, which override vanilla's entries like a data pack's.

Data packs are the server's, not a world's: Obsidian's registries are shared by all its worlds, unlike vanilla's
single world with its `datapacks` folder, so a pack applies to every world.

The order is vanilla's entries, then plugins' files in load order, then data packs' in name order. As in vanilla, the
last file for an entry replaces it whole, and tag files merge (`"replace": true` starts over). Overridable:

- biomes, dimension types and damage types. These are synced: an overridden entry is sent to clients with its data
  rather than by name from the `minecraft:core` pack, like vanilla sends an entry a data pack changed, and it's
  vanilla's again, sent by name, once nothing overrides it. Overriding a biome replaces its climate, effects,
  features, carvers, spawners and spawn costs; [biome modifiers](#biome-modifiers) still apply on top.
- configured and placed features and configured carvers. An overridden feature or carver replaces vanilla's in every
  biome placing it, and vanilla's features naming an overridden one, directly or through others, are read again from
  vanilla's files with the runtime reader, so they use it, as vanilla's do by reference. What places a configured
  feature by identifier, like the pale oak's moss decorator placing `minecraft:pale_moss_patch`, finds it through the
  current features, overrides included, as vanilla looks it up in its registry. Obsidian doesn't grow saplings,
  mushrooms or fungi, or apply bone meal, yet; when it does, they'll find their features the same way.
- processor lists, structures, structure sets, template pools and structure templates. Vanilla's structures, sets and
  pools naming an overridden one, or an overridden placed feature or processor list, are read again the same way, so
  vanilla's structures use the overrides; a pool or template is used by identifier wherever it's named, so an
  overridden one replaces vanilla's in every structure. A structure set override moves where vanilla's structures
  start, in chunks not generated yet.
- noise settings, density functions and noises, read again the same way through vanilla's entries naming them. The
  noises the surface builder reads by name (`minecraft:surface` and `surface_secondary`, the badlands' and icebergs'
  pillar, roof and surface noises, and `minecraft:clay_bands_offset`) follow overrides too: overriding one reads the
  vanilla dimension's stem at runtime, so that it generates with the file's noise.
- parameter lists: `data/minecraft/worldgen/multi_noise_biome_source_parameter_list/overworld.json` with
  `{"preset": "minecraft:nether"}` makes the overworld pick nether biomes from its climate.
- level stems: `data/minecraft/dimension/overworld.json` (or `the_nether`, `the_end`) replaces how that dimension
  generates, read like any [dimension](#dimensions), with an inline `biomes` list, say. It must keep vanilla's
  dimension type (`minecraft:overworld`, `minecraft:the_nether`, `minecraft:the_end`), since Obsidian creates
  vanilla's levels with those; override the type's file to change it. It must keep generating with
  `minecraft:noise` too: Obsidian creates vanilla's levels with vanilla's noise generator, which the overrides
  configure, so a flat, debug or plugin generator there is rejected, saying so (a flat world is a world type of its
  own, see `worlds.json`). A type's build range (`min_y` and `height`)
  must hold the noise settings its vanilla dimension generates with, as overridden: vanilla clamps generation to the
  build range (`NoiseSettings.clampToHeightAccessor`), which Obsidian doesn't yet, as every step reading the noise
  settings' range (terrain fill, base heights, aquifers, surface, carvers, structures) would have to clamp the same
  way, and vanilla's carvers can then reach past the build range, where vanilla's chunks quietly ignore writes;
  vanilla's own pairings never clamp, so there's no capture to check it against. A type override that doesn't fit, like the
  overworld's at `min_y` 0 with vanilla's overworld noise settings from -64, is logged once startup has read every
  plugin and pack, and left out, keeping vanilla's type. Override the noise settings to match it. A stem startup couldn't read yet, like a
  plugin's overworld stem naming noise settings a data pack adds, is read before the types are checked, so a type
  is checked with the noise settings its dimension's stem names. Stems are then read against the types kept, so one
  that couldn't be read with the type override, like an overworld stem using vanilla's noise settings, is read
  again and applies.
- tags, merged as above.

Vanilla's entries nothing overrides keep their compiled code, so without overrides worlds generate exactly as before.
A vanilla dimension whose stem, or the noise settings or parameter list its stem names, is overridden is built at
runtime instead, from the override or from vanilla's own stem, with the same readers as plugins' dimensions.

When overrides apply follows vanilla, which reads a data pack's level stems over those saved in a world's level.dat
on every load, and named noise settings and biomes too: a pack added to an existing world changes the chunks it
generates from then on, while chunks already generated stay, with borders between them. Obsidian saves no stems
(`worlds.json` and level.dat only name the generator), so a vanilla dimension takes its overrides when its level
starts, in every world, and keeps them until the server stops:

A world made in vanilla with a data pack changing its overworld, like Terralith, saves the pack's overworld level
stem in its level.dat. Opened as an integrated server, such a world loads when a loaded data pack or plugin overrides
`minecraft:overworld`'s level stem with the same stem, compared the way vanilla's codecs saved it (numbers at the
precision saved, climate parameters as one number or a pair, identifiers with or without `minecraft:`); vanilla
would read the pack's stem over the saved one anyway. Without an override, or with one that differs, it's refused
with an error saying to add the data pack, or where the stems differ. The nether's and the end's saved stems aren't
compared.

- Data packs, and plugins loaded at startup, apply to every world as it loads.
- A plugin loaded, reloaded or unloaded later follows the plugin rules: its biomes, features, carvers, spawns, tags
  and damage types apply at once (players are sent through configuration when synced entries change), while a change
  to how a running vanilla dimension generates (its stem, the noise it reads, its parameter list) is logged and
  applies after a restart, like a change to a plugin's dimension. A vanilla dimension type keeps its height from the end
  of startup, when the worlds create their levels with it.
- A vanilla dimension built with a plugin's types stops generating when the plugin unloads and resumes when it's
  back, like a plugin's dimension (see [Noise types](#noise-types)).

## Biome regions

A biome region adds a plugin's biomes to a dimension that already exists, like the overworld or the nether, without
replacing its layout, so that several plugins' biomes generate side by side. It's Obsidian's own format, for what
TerraBlender does for NeoForge and Fabric mods: `data/<namespace>/biome_region/<path>.json` is the region
`<namespace>:<path>`. A region is an alternative layout for parts of the dimension: either the dimension's own with
some biomes swapped,

```json
// data/ruby/biome_region/crystal_plains.json
{
  "dimension": "minecraft:overworld",
  "weight": 2,
  "replace": {
    "minecraft:plains": "ruby:crystal_plains",
    "minecraft:sunflower_plains": "ruby:crystal_plains"
  }
}
```

or a parameter list of its own, picked from the climate like a `multi_noise` biome source's `biomes` (see
[Dimensions](#dimensions)):

```json
// data/ruby/biome_region/crystal_lands.json
{
  "dimension": "minecraft:overworld",
  "weight": 1,
  "biomes": [
    {
      "biome": "ruby:crystal_plains",
      "parameters": {
        "temperature": [-1, 1], "humidity": [-1, 1], "continentalness": [-0.11, 1],
        "erosion": [-1, 1], "depth": 0, "weirdness": [-1, 1], "offset": 0
      }
    },
    {
      "biome": "minecraft:ocean",
      "parameters": {
        "temperature": [-1, 1], "humidity": [-1, 1], "continentalness": [-1.2, -0.11],
        "erosion": [-1, 1], "depth": 0, "weirdness": [-1, 1], "offset": 0
      }
    }
  ]
}
```

- `dimension` names the dimension by its level stem: `minecraft:overworld`, `minecraft:the_nether` or a plugin's
  dimension. Regions are meant for dimensions whose biomes follow the climate (`multi_noise`, like the overworld and
  the nether, whatever their parameter list), but apply to any dimension of vanilla's noise generator. A region
  naming a dimension that doesn't exist does nothing.
- `weight`, from 1 to 1000, is how often the region is picked, against the dimension's other regions and its own
  layout, which weighs 10 (like TerraBlender's vanilla regions).
- `replace` maps biomes of the dimension to the biomes generated instead; biomes the dimension doesn't generate are
  never replaced. A key may be a biome tag, `"#c:is_plains": "ruby:crystal_plains"`, which replaces each of the
  tag's biomes, as the tag holds them when the dimension starts generating (a tag that doesn't exist then replaces
  nothing); a biome named itself, or by a tag written before, keeps that replacement. `biomes` is a parameter list,
  the dimension's whole layout where the region is: every climate should find a fitting biome, oceans and caves
  included. A region has one of the two.
- Biomes are vanilla's or plugins' registered ones, the plugin's own or those of plugins loaded before it. Fields the
  format doesn't have are errors. A file that can't be read is logged and left out, and the plugin loads without it.
  When two plugins define the same region, the last in load order wins, as for other entries.

Regions can be defined in code too, in `ConfigureRegistry`, with `IPluginRegistry.RegisterBiomeRegion`: the
identifier (in the plugin's namespace), the dimension, the weight, and either the `replace` map or the `biomes` as
`ClimateBiome` entries (a biome and its climate ranges, a single value being a range of one). A region registered so
is read exactly like a file, once the plugin's biomes are registered, and follows the same rules; one that can't be
read is logged and left out.

```csharp
public override void ConfigureRegistry(IPluginRegistry registry)
{
    registry.RegisterBiomeRegion(
        "ruby:crystal_plains",
        "minecraft:overworld",
        2,
        new Dictionary<string, string> { ["#c:is_plains"] = "ruby:crystal_plains" });
}
```

Each column's region is picked from the world seed and the dimension: the dimension is split into irregular cells
about 1024 blocks across, each of which picks a region, or the dimension's own layout, at random by weight. A world
generates the same regions every time. Terrain doesn't depend on biomes, so only biomes change, with what follows
them: surface rules, features, carvers, spawns and structures. A region's biomes count among the dimension's possible
biomes, whose features are ordered together (when they can't be, biome modifiers are left out as for any dimension,
see [Biome modifiers](#biome-modifiers)), and structures generate in them where their biome tags allow. A dimension
with no regions generates exactly as without them.

Region biomes are plain biomes, so tags group them with the dimension's other biomes. Add them to the tags that biome
modifiers and other plugins name, like `#c:is_overworld` and `#minecraft:is_overworld` (or `#c:is_nether` and
`#minecraft:is_nether`), and to vanilla's structure tags for the structures they should have, like
`#minecraft:has_structure/village_plains`:

```json
// data/c/tags/worldgen/biome/is_overworld.json, and the same in data/minecraft/tags/worldgen/biome/
{ "values": ["ruby:crystal_plains"] }
```

The rules:

- Regions are part of a dimension's generator. A dimension takes the loaded plugins' regions when it starts
  generating (at startup for vanilla's, when it's added for a plugin's), and keeps them until the server stops, so
  that its chunks all generate with the same layout. A plugin loading, reloading or unloading later doesn't change
  the regions of a dimension that generates already; the change is logged, and applies after a restart. A dimension
  whose generator can't be built, and whose level is left out, lets go of its regions.
- A region whose plugin unloads keeps generating with its biomes, which stay registered (see
  [Biomes](#biomes-dimension-types-and-damage-types)), without the plugin's features, which go with it. Regions are
  data, so they never keep a plugin's code loaded.
- Adding or removing a dimension's regions moves where its regions generate in chunks not generated yet, since the
  cells pick again; chunks already generated stay as they are. After a restart without the plugin, its biomes in
  saved chunks load as plains.

## Level generators

A plugin can generate a whole level in code, like the host's own generators: a class implementing `ILevelGenerator`,
registered by identifier in `ConfigureRegistry`.

```csharp
public sealed class SkyIslandsGenerator : ILevelGenerator
{
    public string Id => "ruby:sky_islands";

    public void Init(ILevel level)
    {
    }

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        // Fill the chunk's blocks and biomes, then mark it generated (full).
    }
}

public override void ConfigureRegistry(IPluginRegistry registry) =>
    registry.RegisterGenerator<SkyIslandsGenerator>("ruby:sky_islands");
```

A world uses it by naming it as its `Generator` in `worlds.json`, and a world's child dimension by its name there,
which is also its dimension type (vanilla's or a plugin's), like `minecraft:the_nether` names the nether's generator.
A level stem names it as its `generator`, with settings, like `{"type": "ruby:sky_islands", "island_size": 12}` (see
[Dimensions](#dimensions)). To take them, implement `IConfigurableLevelGenerator`, whose `Configure` gets the stem's
generator object (its `type` included) on each new instance, before `Init`:

```csharp
public sealed class SkyIslandsGenerator : IConfigurableLevelGenerator
{
    private int islandSize = 8;

    public void Configure(JsonElement settings)
    {
        if (settings.TryGetProperty("island_size", out var size))
            this.islandSize = size.GetInt32();
    }

    // Id, Init and GenerateChunkAsync as above.
}
```

Settings it can't use should throw: the level then stops, logged, like when `Init` throws.

The rules:

- The identifier is in the plugin's namespace, and registering is only possible in `ConfigureRegistry`. Identifiers
  the host registered (`overworld`, `superflat`, ...) are the host's. A generator registered with the plugin's
  `ILevelFactory` (injected, or from its services) instead is the plugin's too, under its `Id`, and follows these
  rules except the namespace: the first plugin to register an id keeps it. It may also be registered in the plugin's
  load callbacks.
- Each level using the generator gets its own instance, created in a service scope of its own (constructor
  injection works like for the plugin's other services, scoped ones included), and its `Init` is called with the
  level. Once the level stops using it (the plugin unloads, the generator fails, or the level is disposed, which
  stops it for good), the instance is disposed if
  it's `IDisposable` or `IAsyncDisposable`, then its scope, after the calls using it end: release there what `Init`
  subscribed to, or the plugin's code can't unload. The level saves the
  registered identifier as its generator (`generatorName` in its `level.dat`); the instance's own `Id` isn't used.
- Every call into the generator (creating it, `Init`, `GenerateChunkAsync`, `FindSpawnPointAsync`, `LockChunkAsync`)
  is a call into the plugin, a chunk at a time, so unloading the plugin waits for chunks being generated.
- Generating a chunk and saving it hold the level's own lock on that chunk, so a save never captures a chunk
  mid-write, even while the generator stops or is replaced. A save also takes the generator's `LockChunkAsync` lock
  while it's the level's generator: lock with it any chunk the generator writes into besides the one it generates.
  Once it's stopped, replaced or failed, saves wait for its running calls to end instead. A newly started instance
  (on load, reload or once its plugin loads) doesn't generate until the saves already in progress, which may not
  have taken its lock, are done.
- When the plugin unloads, the level stops generating new chunks, with a warning, and lets go of the generator, so the
  plugin's code can unload. Chunks already generated still load and are saved; ones players reach meanwhile stay
  ungenerated. When the plugin loads again, the level gets a new instance of the plugin's new generator, and generates
  from its next chunk: a chunk being generated during the swap finishes with the generator it started with, and
  chunks generated before stay as they are.
- When the generator throws, the error is logged naming the plugin, the chunk is left ungenerated and empty (without
  what the generator wrote into it), and the level stops generating until the plugin reloads, rather than stopping
  chunk management or the server.
- After a restart without the plugin, a world saved with its generator loads stopped, with a warning, rather than
  generating with another generator; its chunks still load, and it generates again once a plugin registers the
  generator. A new world or dimension can't be created without its generator: the server refuses to start it, saying
  which generator is missing. A dimension whose creation was cancelled before it saved its level data or any chunk
  counts as new. An unknown generator identifier is never replaced by the overworld's.

## Client

A mod whose side is `client` or `both` also runs on the Obsidian client, which registers its content like a server
(see [mod-system.md](mod-system.md)). Its plugin class implements `IClientPlugin` to register its client side. The
client API is its own assembly and package, `Obsidian.API.Client`, which the mod references besides `Obsidian.API`;
it's interfaces, data and Razor building blocks (with the ASP.NET Core framework they're built on), and servers ship
it too, so the same plugin class loads on them. The client calls
`ConfigureClient` when it loads its mods, after `ConfigureRegistry`: at startup, and on a new instance of the plugin
when the player reloads them (see below); servers never call it, so a `both` mod runs on a server without any client
code.

What `ConfigureClient` registers takes effect when it returns and stays until the client exits or reloads its mods;
if it throws, the mod fails to load and none of it does. The registry only takes registrations during
`ConfigureClient`, since the client loads the assets that depend on them afterwards: registering later, through a
registry the mod kept, throws `InvalidOperationException`. Its `Connection`, and sending payloads on it, stay usable
for as long as the client runs. Like the server's content, key bindings, tints, renderers, particle providers,
block entity shape and model providers, block entity renderers, item model properties, item renderers, dimension
effects and menu screens are the mod's own: their identifiers are in its namespace (its mod id) and valid by
vanilla's rules, they're for content it registered, and registering one twice throws `ArgumentException`.

`IClientRegistry.RegisterBlockEntityShapes(string blockEntityType,
Func<IReadOnlyDictionary<string, object?>, BlockShapes?> provider)` gives a mod's block entity type its collision
and outline shapes from its latest synced update data. Register the type in `ConfigureRegistry` on both sides
with `RegisterBlockEntity<T>`, then its provider in `ConfigureClient`. The provider receives plain NBT values:
numbers, strings and arrays, nested compounds as dictionaries and lists as `List<object?>`. The outer dictionary
is read-only, and each provider receives its own deep copy, cached per snapshot, so changing nested values cannot
change the world or another provider's data. Keep the provider pure and thread-safe, since it runs on a reading
thread, at most once per snapshot. A new chunk or update packet replaces the snapshot and recomputes the shapes.
Picking, the selection outline and local player collision all use them, independently of resource-pack models.
Returning null or throwing uses the state's usual shapes; an exception is logged. Providers are staged like the
other client registrations and released on unload, while the shared block entity type keeps its id.

```csharp
registry.RegisterBlockEntityShapes("ruby:cable_bus", data =>
{
    var connected = data.TryGetValue("connected", out var value) && value is byte flag && flag != 0;
    BoundingBox[] boxes = connected
        ? [new(new VectorD(0.375, 0.375, 0), new VectorD(0.625, 0.625, 1))]
        : [new(new VectorD(0.375), new VectorD(0.625))];

    return new BlockShapes(boxes, boxes);
});
```

Use the same pure shape-building helper in the server's `GetShapes` and the client provider to keep them equal.

`IClientRegistry.RegisterBlockEntityModel(string blockEntityType, BlockEntityModelProperties model)` adds model
parts from a block entity's synced data to its block's model as the world is meshed, like NeoForge's model data:
a cable's arms, the parts on its sides, facades. `Models` declares the block models the parts may name (for
example `ruby:part/cable_side` for `assets/ruby/models/part/cable_side.json`), which the client reads with the
block models; `Provider` receives the same plain NBT values as a shape provider and the block's `IBlock` state,
and returns a `BlockModelData`, or null for the block state's model alone:

- `Parts`: `BlockModelPart(Model, X, Y, UvLock)`, a declared model turned like a blockstate file's model (quarter
  turns, X then Y, its cull faces turning with it).
- `Faces`: `BlockModelFace(From, To, Face, Texture)`, one face of a box in sixteenths of a block, like a model
  element's face without `uv`, with any block texture (`minecraft:block/stone`), an optional `CullFace`,
  `TintIndex`, `Shade` and `LightEmission` (0 to 15, like a model element's `light_emission`: the face is lit at
  least that brightly, a screen glowing in the dark). They're for textures chosen by data, like a facade showing
  another block's texture.
- `ReplacesBlockModel`: the parts and faces replace the block state's model rather than adding to it, for a block
  drawn entirely from its data, or whose own model a facade covers.

The parts and faces are meshed like the block's own model: lit with smooth lighting and ambient occlusion, shaded
by direction, tinted by the block's tint where they have a tint index, put in the render layer of their texture
(or their model's `render_type`), raised to their light emission, and culled where their cull face's neighbour is
opaque. The model files' render types and emissive elements are described in
[plugin-archives.md](plugin-archives.md#assets). They don't change how the block culls its neighbours,
which stays the block state's. The provider runs on a meshing thread inside the mod's call guard, at most once per
update and block state, so keep it pure; a part naming an undeclared model or a rotation that isn't a quarter turn
counts as a failure, which is logged and draws the block state's model. Invalid face or cull directions are rejected
in the provider call. Malformed declared parts are cached as failures and fall back for that block alone, without
discarding the section's mesh. Each new update meshes the block's section again. The provider sees only its own
block: a model that follows its neighbours, like a cable connecting to them, has the server work out the connections,
put them in `GetUpdateData` (or in block state properties) and
`SendUpdate` when they change, as the shapes do. Connections alone need no block entity: the engine sample's cable
(`samples/EngineSample`) keeps them in block state properties, set by its `GetStateForPlacement` and `UpdateShape`
hooks, and its multipart blockstate draws an arm for each.

```csharp
string[] sides = ["north", "east", "south", "west"];
registry.RegisterBlockEntityModel("ruby:cable_bus", new BlockEntityModelProperties
{
    Models = ["ruby:part/cable_core", "ruby:part/cable_side"],
    Provider = (data, block) =>
    {
        // The server's update data says which sides connect, and which side has a facade of which texture.
        var parts = new List<BlockModelPart> { new("ruby:part/cable_core") };
        for (var i = 0; i < sides.Length; i++)
        {
            if (data.TryGetValue(sides[i], out var value) && value is byte connected && connected != 0)
                parts.Add(new("ruby:part/cable_side", Y: i * 90));
        }

        BlockModelFace[] faces = data.TryGetValue("facade", out var facade) && facade is string texture
            ? [new(new VectorF(0, 0, 0), new VectorF(16, 16, 1), BlockFace.North, texture)
                { CullFace = BlockFace.North }]
            : [];

        return new BlockModelData
        {
            Parts = parts,
            Faces = faces,
            ReplacesBlockModel = true
        };
    }
});
```

`IClientRegistry.RegisterBlockEntityRenderer(string blockEntityType, BlockEntityRendererProperties renderer)` draws
a block entity type's moving parts each frame, like vanilla's block entity renderers: a machine's gear spinning while
it works, a door sliding open. Model data is meshed with the world and costs nothing per frame, but only changes
with an update; a renderer's parts move between updates and ticks. Like model data, `Models` declares the models the
parts may name, which the client reads with the block models (block or item models with elements, in the block model
format). A renderer has a provider, code, or both:

- `Provider` receives the block entity's latest synced update data and its block's `IBlock` state, like a model
  provider, and returns the `BlockEntityRenderPart`s to draw, or null for none: a part is shown by returning it and
  hidden by leaving it out. It runs at most once per update and block state, and the client animates its parts each
  frame without calling the mod. A part draws a declared `Model` (a `BlockModelPart`, turned in quarter turns like
  model data's) and `Faces` (model data's faces, without culling or tints), scaled by `Scale` and turned about
  `Pivot` (in sixteenths of a block, the block's centre by default) around `Axis` (up by default), then moved by
  `Offset` (in sixteenths). Its angle is `Angle` degrees plus `Speed` degrees per tick since the game tick `Since`,
  counting the partial tick, so it turns smoothly between ticks. The server's level counts the ticks (`ILevel.Time`),
  which the client follows: a machine that starts working puts the tick in its update data.
- `Renderer` is code, an `IBlockEntityRenderer`, whose `Render(BlockEntity blockEntity, float partialTick,
  IBlockEntityRenderBuffer buffer)` runs each frame for each block entity, like vanilla's `BlockEntityRenderer.render`.
  It gets the mod's live client instance (see `BlockEntity.ClientTick` in [Block entities](#block-entities)), from its
  first client tick on, and draws declared models (`buffer.Model(part, transform)`) and faces (`buffer.Face(face,
  transform)`) with `Matrix4x4` transforms in blocks from the block's lowest corner. Frames are built on the game
  thread, which also runs `ClientTick`, so it reads what ticks keep in the instance's fields without locking and
  interpolates between the last two ticks with `partialTick`: for what a provider's parts can't describe, like
  animations following client-side state. Keep it short and free of allocations, as the frame waits for it.

Both run inside calls into the mod. If one throws, the error is logged and the block entity draws nothing from it
(what code drew that frame is taken back) until its next update. Face directions are checked inside the provider
call, and malformed declared parts stop that block entity's drawing without breaking the frame. Parts are lit with
the light at the block, like vanilla's block entities, raised to their faces' light emission, shaded by the direction
they face as they turn, and put in the render layer of their texture or model. Block entities are drawn within 64
blocks of the camera (vanilla's default for its renderers) and while in view. The client only has a block entity's
data once the server sends it, with its chunk or an update, so a type with a renderer should return update data from
`GetUpdateData`, even an empty compound, to be drawn as soon as it's placed.

```csharp
// The grinder spins its wheel while it works: the server puts "working_since" in its update data (the level's Time
// when it started) and leaves it out when it stops, sending an update each time.
registry.RegisterBlockEntityRenderer("ruby:grinder", new BlockEntityRendererProperties
{
    Models = ["ruby:part/grinder_wheel"],
    Provider = (data, block) =>
    {
        // A wheel facing north turns about the north-south axis; one facing east is turned a quarter turn first.
        var east = block.GetProperty("facing") is "east" or "west";
        var wheel = new BlockModelPart("ruby:part/grinder_wheel", Y: east ? 90 : 0);
        if (data.TryGetValue("working_since", out var value) && value is long since)
        {
            return
            [
                new BlockEntityRenderPart
                {
                    Model = wheel,
                    Axis = east ? new VectorF(1, 0, 0) : new VectorF(0, 0, 1),
                    Speed = 18,
                    Since = since
                }
            ];
        }

        return [new BlockEntityRenderPart { Model = wheel }];
    }
});
```

A mod's items are drawn from their item model definitions, `assets/<ns>/items/<item>.json`, like vanilla's since
1.21.4 (see the item models bullet below). Like NeoForge's item model registration events, the mod adds what those
definitions can name:

- `IClientRegistry.RegisterItemCondition(string property, Func<ItemStack, ItemModelContext, bool> condition)`,
  `RegisterItemSelect(string property, Func<ItemStack, ItemModelContext, string?> select)` and
  `RegisterItemRange(string property, Func<ItemStack, ItemModelContext, float> range)` register a property a
  `minecraft:condition`, `minecraft:select` or `minecraft:range_dispatch` model names in its `property`, like
  `"property": "ruby:charge"`. A select draws the case whose `when` is the value (or lists it), compared exactly, and
  its `fallback` for null; a range the entry with the highest `threshold` at or below the value times `scale`, else
  its `fallback`. Like vanilla's, the three kinds are separate, so one identifier can name one of each. A property is
  a function of the stack, read-only, with its components, including the mod's own (read with `ItemStack.TryGet`,
  see [Item components](#item-components)), and of an `ItemModelContext`: the `DisplayContext` (`Gui`, `Ground`,
  `Fixed`, `Head`, and the first and third person hands), the `HolderId` of the entity holding, wearing or showing
  the stack, or the dropped item (the player for their inventory; `IClientWorld.GetEntity` takes it), and the world's
  `GameTime` and `DayTime` in ticks.
- `IClientRegistry.RegisterItemRenderer(string specialModelType, ItemRendererProperties renderer)` draws a
  `minecraft:special` model type in code, like NeoForge's special model renderers: a definition's
  `{"type": "minecraft:special", "model": {"type": "ruby:gem_display"}, "base": "ruby:item/gem"}` draws with the
  type's `IItemRenderer`, whose `Render(ItemStack stack, ItemModelContext context, IItemRenderBuffer buffer)` draws
  declared `Models` (`buffer.Model(part, transform)`) and faces (`buffer.Face(face, transform)`) like a block entity
  renderer's code, with `Matrix4x4` transforms in blocks in the item's model space (the box from 0 to 1 its base
  model's elements fill). The base model's `display` transform for the context then places it, in every context:
  menus, hands, heads, item frames and the ground. Fields of the special model other than its `type` aren't read.

The properties run on the client's game thread inside calls into the mod, and their value is kept per stack and
display context until the stack changes or its context does (another tick, holder or display context), so a
property reading the mod's component decodes it about once a tick rather than every frame: keep them pure. A property
that throws is logged and gives its default (false, the fallback, 0) for the stack until the stack changes. The
renderer runs each frame for each stack drawn with its type, on the game thread inside a call into the mod: keep it
short and free of allocations. If it throws, the error is logged, what it drew is taken back, and the base model is
drawn instead until the stack changes; the base model is drawn too once the mod is gone. Both go when the mod
unloads, and definitions naming them fall back to their defaults.

```csharp
// The gem's definition, assets/ruby/items/gem.json, selects by its charge: plain, glowing, or full, which the gem
// display renderer draws in code.
// {"model": {"type": "minecraft:select", "property": "ruby:charge",
//   "cases": [
//     {"when": "charged", "model": {"type": "minecraft:model", "model": "ruby:item/gem_glowing"}},
//     {"when": "full", "model": {"type": "minecraft:special", "base": "ruby:item/gem",
//       "model": {"type": "ruby:gem_display"}}}],
//   "fallback": {"type": "minecraft:model", "model": "ruby:item/gem"}}}
// this.charge is the PluginComponentType<int> the mod registered in ConfigureRegistry.
registry.RegisterItemSelect("ruby:charge", (stack, context) => stack.Get(this.charge) switch
{
    >= 100 => "full",
    > 0 => "charged",
    _ => null
});
registry.RegisterItemRenderer("ruby:gem_display", new ItemRendererProperties
{
    Models = ["ruby:item/gem_core"],
    Renderer = new GemDisplayRenderer()
});

// The full gem's core spins about its centre with the world's time.
public sealed class GemDisplayRenderer : IItemRenderer
{
    public void Render(ItemStack stack, ItemModelContext context, IItemRenderBuffer buffer)
    {
        var spin = Matrix4x4.CreateTranslation(-0.5f, -0.5f, -0.5f)
            * Matrix4x4.CreateRotationY(context.GameTime % 360 * MathF.PI / 180)
            * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f);
        buffer.Model(new BlockModelPart("ruby:item/gem_core"), spin);
    }
}
```

```csharp
public sealed class RubyMod : PluginBase, IClientPlugin
{
    public override void ConfigureRegistry(IPluginRegistry registry)
    {
        registry.RegisterBlock("ruby:mossy_ruby");
        registry.RegisterEntity("ruby:golem", new EntityProperties { Width = 1.4f, Height = 2.7f });
        registry.RegisterParticle("ruby:spark");
        registry.RegisterMenu("ruby:pouch", rows: 1, columns: 5);
        registry.MapPayload<SummonGolem>(PayloadPhase.Play, PayloadDirection.Bidirectional, SummonAsync);
    }

    public void ConfigureClient(IClientRegistry registry)
    {
        registry.HandlePayload<SummonGolem>((summon, connection) =>
        {
            // The server summoned a golem; this runs on the connection's packet thread.
        });

        var summon = registry.RegisterKeyBinding("ruby:summon_golem", "key.keyboard.r", "ruby:golems");
        summon.Pressed += () => registry.Connection.SendPayload(new SummonGolem());

        var held = 0;
        registry.OnTick(() =>
        {
            // Every client tick, on the game's thread: holding the key for five seconds summons another.
            held = summon.IsDown ? held + 1 : 0;
            if (held == 100)
                registry.Connection.SendPayload(new SummonGolem());
        });

        registry.RegisterBlockTint("ruby:mossy_ruby", BlockTint.Foliage);
        registry.RegisterEntityRenderer("ruby:golem", new EntityRendererProperties
        {
            Model = "ruby:golem",
            Texture = "ruby:entity/golem",
            ShadowRadius = 0.7f,
            IdleAnimation = "ruby:golem/idle",
            WalkAnimation = "ruby:golem/walk"
        });
        registry.RegisterParticleProvider("ruby:spark", new ParticleProviderProperties
        {
            Lifetime = 20,
            Gravity = 0.5f
        });
        registry.RegisterMenuScreen("ruby:pouch", new MenuScreenProperties
        {
            Texture = "ruby:gui/container/pouch",
            Slots = [new(44, 20), new(62, 20), new(80, 20), new(98, 20), new(116, 20)]
        });
    }
}
```

- **Payloads.** `HandlePayload` handles what the server sends on a channel the mod declared with `MapPayload` for
  payloads to the client (`Clientbound` or `Bidirectional`), in the phases it declared; the handler given to
  `MapPayload` is the server's and isn't called on the client. Handlers run on the connection's packet thread with
  the connection the payload arrived on, whose `Phase` says where it is and whose `SendPayload` answers.
  `registry.Connection` is the connection to whichever server the client is playing on, to send from elsewhere.
  `SendPayload` returns false while the client isn't connected, or when the server didn't agree on the channel for
  the current phase.
- **Key bindings.** A binding `ruby:summon_golem` is named `key.ruby.summon_golem`, its translation key, and saved in
  `options.txt` as `key_key.ruby.summon_golem` like vanilla's; its category `ruby:golems` is translated from
  `key.category.ruby.golems` (a category can be in any namespace, so mods can share one). Mods' bindings are listed
  after vanilla's in the key binds screen, where players rebind them. Keys use vanilla's names: `key.keyboard.r`,
  `key.keyboard.left.alt`, `key.mouse.middle`, or `key.keyboard.unknown` for none. `Pressed` is raised on the
  game's thread when the key is pressed in game with no screen open, `IsDown` stays true until the key that pressed
  it is released (even if the binding is rebound meanwhile), and `Key` is the key it's bound to now. A key bound to a
  vanilla binding and a mod's does both, the screenshot and fullscreen keys included.
- **Ticks.** `OnTick` runs a handler on the game's thread every client tick, 20 times a second, in menus as in game,
  like NeoForge's client tick event: for client logic such as polling `IsDown`. `Connection.Phase` says whether the
  client is playing on a server. A handler that throws is logged and not run again. A mod's block entities tick
  themselves with `BlockEntity.ClientTick` (see [Block entities](#block-entities)).
- **World queries.** `registry.Connection.World` is an `IClientWorld` while playing, null during login,
  configuration and disconnection. The connection passed to a payload handler supplies the same view. A client
  block entity uses `this.GetClientWorld()` from `Obsidian.API.Client`; it is attached before `Load` and detached
  on removal. It is separate from `Level`, whose server operations do not apply to a client view.
  `IsChunkLoaded(chunkX, chunkZ)` takes chunk coordinates (`blockX >> 4`, `blockZ >> 4`). `GetBlock(Vector)` returns
  the current `IBlock`, vanilla or plugin, or air when unloaded or outside the dimension's height. Queries never
  load chunks. `GetBlockEntityData(Vector)` returns a deep `NbtCompound` copy of synced update data, or null;
  nested arrays and compounds belong to the caller. `GetBlockEntity(Vector)` returns a mod's live client instance
  when one exists; cast to your mod's class. It becomes available on the first client tick, follows data updates on
  subsequent ticks, and disappears when its block, chunk or mod goes away.
  `GetEntity(id)` and `GetEntities(BoundingBox)` return detached `IClientEntity` snapshots with `Id`, `Uuid`,
  `Type` (the client's `EntityType` id, including plugin types), `Position` and `Metadata`. Box queries test current
  displayed positions, including the boundary, rather than hitboxes. Use a box centred on a position for nearby
  entities. Removed entities are excluded. Metadata is indexed by protocol byte index with values as decoded:
  scalars, vectors, NBT dictionaries/lists, arrays, item stacks and the client's other decoded values. The table is
  read-only, and mutable values are deep copies: changing them affects neither tracking nor later queries. Missing
  metadata means it has not been received; null is a valid value. Results are eager, with unspecified order.
  Block, chunk and synced block entity data queries may run on any thread; separate calls can observe different
  updates. Entity queries and live instance access require the game's thread and throw elsewhere, so a payload
  handler may read block data there but must hand entity work to its game-thread code. Ticks, key bindings and
  screens run on that thread. Never hand live instances to workers. Obtain `Connection.World` again after changing
  servers instead of retaining it across disconnects.
- **Block tints.** A tinted block's faces whose model gives them a `tintindex` are multiplied by the tint, like
  vanilla's block colours: the biome's grass, foliage, dry foliage or water colour, blended across nearby biomes with
  the player's biome blend (grass follows the swamp and dark forest modifiers), or `BlockTint.Fixed(0xRRGGBB)`. Its
  item is tinted too, on every `tintindex`, unless its item definition gives the model `tints` of its own (which
  then apply by index): with vanilla's grass block item colour (the grass colormap at temperature 0.5 and downfall
  1), its default foliage colour (`#48B518`), dry foliage colour (`#5C3C32`) or water colour (`#3F76E4`), or the
  fixed colour.
- **Animated textures.** A block or item texture with a `.png.mcmeta` animation plays like vanilla's water, lava and
  fire, with no code: its frames in the order and for the ticks the animation gives, blended into each other with
  `interpolate`, advancing once in each frame that runs a client tick, in menus too, and stopping while a server
  freezes the game. The format is in [plugin-archives.md](plugin-archives.md#assets). Faces using it keep their
  texture coordinates, since each frame is copied over the texture's place in the atlas.
- **Item models.** Items are drawn from their item model definitions, `assets/<ns>/items/<item>.json`, as vanilla
  does since 1.21.4, the same way for mods' items and vanilla's, and for a `minecraft:item_model` component naming
  another definition: in menus, hands, heads, item frames and on the ground. `minecraft:model`, `composite`,
  `condition`, `select`, `range_dispatch`, `special` and `empty` models are evaluated for the stack each time it's
  drawn, with vanilla's properties that a stack, its holder and the world's time decide (`broken`, `damaged`,
  `has_component`, `custom_model_data` and `using_item` conditions; `display_context`, `context_dimension`,
  `context_entity_type`, `main_hand`, `block_state`, `charge_type`, `custom_model_data`, `trim_material` and
  `local_time` selects; `custom_model_data`, `damage`, `count`, `use_duration` and `time` ranges) and the mod's own
  (see the item model properties above). Vanilla's other properties give their defaults (false, the fallback, 0),
  and `bundle/selected_item` draws nothing. A `minecraft:special` model of a type with a renderer (vanilla's chest,
  shield, bed, banner, head and the like, or a mod's) is drawn by it, and otherwise by its `base` model.
- **Particle providers.** A mod's particle type is drawn with the sprites of its definition,
  `assets/<ns>/particles/<name>.json` (`{"textures": ["<ns>:spark_0", "<ns>:spark_1"]}`, from
  `assets/<ns>/textures/particle/`), one picked at random per particle or, with `AnimateSprite`, in turn over its life.
  Without a provider its particles behave like vanilla's base particle: they live 4 to 40 ticks, are 0.1 to 0.2 blocks
  wide, drift in a random direction around the velocity sent, and slow down without falling.
  `ParticleProviderProperties` sets their `Lifetime` in ticks, `Gravity` (vanilla's: 1 speeds their fall by 0.04
  blocks per tick each tick), `Friction`, `Scale`, and whether to keep the server's velocity
  (`RandomizeVelocity = false`).
- **Menu screens.** A mod's menu type shows like a chest without a screen: vanilla's chest background with its rows,
  their slots centred, and the player's inventory below. `MenuScreenProperties` gives it a background instead, from
  `assets/<ns>/textures/gui/...` (`Texture = "<ns>:gui/container/pouch"`, its top left `Width` by `Height` pixels),
  with a position for each of the menu's slots, in order, and for the player's inventory, the title and the
  "Inventory" label. Its slots must be as many as the menu has. A menu with more than slots on a background
  (progress, buttons, lists) gets a screen in code instead (see [Screens in code](#screens-in-code)), which reads the
  extra data the menu was opened with ([Machine menus](#machine-menus)) and its data slots, and clicks its buttons. A
  menu type has one screen or the other.
- **Entity renderers.** A mod's entity type is drawn with a model layer from `assets/<ns>/models/entity/<path>.json`
  (`Model = "<ns>:<path>"`) and a texture from `assets/<ns>/textures/entity/...` (`Texture = "<ns>:entity/..."`, or
  `minecraft:entity/...` for vanilla's), scaled by `Scale`, with a shadow of `ShadowRadius` blocks. Without a
  renderer, or with a model that's missing or doesn't parse (which is logged), its entities aren't drawn. Entities
  are placed like vanilla's living entities: turned to their body's yaw and tilted as they die or sleep. Parts named
  `head` turn to where the entity looks, and parts whose names end in `leg` swing as it walks (left and right, and
  `front` and back, in opposite phases); other parts stay as modelled. Vanilla's rules hiding parts by name, like
  `left_chest` without a chest, don't apply. Keyframed animations from `assets/<ns>/animations/entity/<path>.json`
  (`"<ns>:<path>"`, format below) move parts on top of that: `IdleAnimation` plays from the entity's spawn,
  `WalkAnimation` advances with the distance it walks and blends in with its speed like vanilla's walking animations
  (and replaces the leg swing), and `AttackAnimation` plays once each time it swings its arm, which servers show as
  mobs attack. A missing or malformed animation is logged and not played. What the server says the entity holds is
  drawn in the hands of parts named `right_arm` and `left_arm`, and vanilla's armour it wears on the parts vanilla's
  humanoid armour covers (`head`, `body`, `right_arm`, `left_arm`, `right_leg`, `left_leg`), following their poses;
  a model without such a part doesn't show what goes on it. The entity type's registered `Width` and `Height` are its
  hitbox, for picking and hitting it, its name tag's height and its eye height (85% of its height, vanilla's default).
  Riders sit at its type's `PassengerAttachments` (see [Entities](#entities)).

A renderer can choose its texture from the entity's synced metadata, draw layers over its model and run code, like
vanilla's renderers:

- `TextureProvider` receives the entity's metadata, as `IClientEntity.Metadata` has it (by protocol index, values as
  decoded), and returns a texture, or null for `Texture`: for variants.
- `Layers` are `EntityRenderLayer`s drawn over the model in order, like vanilla's render layers: a model layer
  (`Model`, or null for the entity's own model), a `Texture`, a `Tint` (`0xRRGGBB`) and a `RenderType`. `Cutout`, the
  default, is drawn like the model; `Translucent` blends by the texture's alpha; `Emissive` is vanilla's `eyes`:
  full-bright whatever the light, unshaded, never tinted red as the entity is hurt, and blended without hiding what's
  behind it, for glowing eyes and markings. A layer's parts are posed by name like the model's (heads turn, legs
  swing, animations move them), so a saddle or armour layer whose parts are named like the model's follows it. A
  layer's `Provider` chooses its look from the metadata: an `EntityLayerAppearance` with its texture and tint, or
  null to hide it.
- `Renderer` is code, an `IEntityRenderer`, whose `Render(in EntityRenderContext entity, IEntityRenderBuffer buffer)`
  runs each frame after the model and layers, like vanilla's `EntityRenderer.render`. The context has the entity's id,
  type and metadata, the partial tick, its age in ticks and its position, body and head yaw and pitch, interpolated
  for the frame, and `ModelTransform`, which its model is drawn with. `buffer.Model(model, part, texture, transform,
  renderType, tint)` draws a model layer, or one part of it with its children: the renderer's model, a layer's, or
  one of `Models`, which declares more model layers for the client to read. Its `Matrix4x4` takes the model's space
  (16 pixels to a block, Y down, or the part's parent's for a part) to blocks from the entity's position;
  `ModelTransform` places it like the entity's model. Its parts are posed by name like the model's too. Keep it short
  and free of allocations, as the frame waits for it.

The providers run at most once per metadata change, the code each frame, all on the client's game thread inside
calls into the mod; the metadata table is the entity's own, read-only, so don't change values in it. A provider that
throws is logged and its defaults (`Texture`, or a layer's `Texture` and `Tint`) are used until the metadata changes
again; code that throws is logged, what it drew that frame is taken back, and it isn't called for the entity until
its metadata changes. Layers and code aren't drawn while the entity is invisible. They go when the mod unloads.

```csharp
registry.RegisterEntityRenderer("ruby:golem", new EntityRendererProperties
{
    Model = "ruby:golem",
    Texture = "ruby:entity/golem",

    // The server keeps the golem's ore in metadata 16 (a VarInt): 1 for gold.
    TextureProvider = data => data.GetValueOrDefault((byte)16) is 1 ? "ruby:entity/golem_gold" : null,
    Layers =
    [
        new EntityRenderLayer
        {
            Texture = "ruby:entity/golem_eyes",
            RenderType = EntityRenderType.Emissive
        },
        new EntityRenderLayer
        {
            Model = "ruby:golem_saddle",
            Texture = "ruby:entity/golem_saddle",
            Provider = data => data.GetValueOrDefault((byte)17) is true
                ? new EntityLayerAppearance("ruby:entity/golem_saddle")
                : null
        }
    ],
    Models = ["ruby:golem_crystal"],
    Renderer = new GolemCrystalRenderer()
});

// A glowing crystal over the golem's head, where its model layer puts it, spinning with the golem's age.
public sealed class GolemCrystalRenderer : IEntityRenderer
{
    public void Render(in EntityRenderContext entity, IEntityRenderBuffer buffer)
    {
        var spin = Matrix4x4.CreateRotationY(entity.Age * 0.1f) * entity.ModelTransform;
        buffer.Model("ruby:golem_crystal", null, "ruby:entity/golem_crystal", spin, EntityRenderType.Emissive);
    }
}
```

Providers read the fields every mob has, like its custom name, health or flags, and the type's own synced data
fields (see [Synced data fields](#synced-data-fields)) with `TryGetData`.

A model layer has the shape of the layers in the client's `client_models.json`, which are vanilla's model layers:

```json
{
  "width": 64,
  "height": 32,
  "root": {
    "x": 0, "y": 0, "z": 0, "xRot": 0, "yRot": 0, "zRot": 0, "xScale": 1, "yScale": 1, "zScale": 1,
    "cubes": [],
    "children": {
      "head": {
        "x": 0, "y": 4, "z": 0, "xRot": 0, "yRot": 0, "zRot": 0, "xScale": 1, "yScale": 1, "zScale": 1,
        "cubes": [[
          [[-4, -8, -4, 0.125, 0.25], [-4, 0, -4, 0.125, 0.5], [4, 0, -4, 0.25, 0.5], [4, -8, -4, 0.25, 0.25]]
        ]],
        "children": {}
      }
    }
  }
}
```

`width` and `height` are the texture's size in pixels. Each part has its pivot (`x`, `y`, `z`, in pixels from its
parent's), its rotation in radians and its scale, its cubes, and its child parts by name. A cube is a list of faces,
and a face four vertices `[x, y, z, u, v]`: a position in pixels from the part's pivot and a texture coordinate as a
fraction of the texture's width and height. Model space is vanilla's: 16 pixels to a block, Y pointing down, and the
entity's feet at Y 24.

An animation has the shape of the animations in `client_models.json`, which are vanilla's keyframed entity
animations:

```json
{
  "length": 2,
  "loop": true,
  "channels": [
    {
      "bone": "head",
      "target": "ROTATION",
      "keys": [
        { "time": 0, "pre": [0, 0, 0] },
        { "time": 1, "pre": [0.5, 0, 0], "cubic": true },
        { "time": 2, "pre": [0, 0, 0], "cubic": true }
      ]
    }
  ]
}
```

`length` is in seconds, and a looping animation (`loop`, false if left out) repeats after it, while one that doesn't
holds its last pose. Each channel moves the part named `bone` by its `target`: `POSITION` in pixels (Y down, as in
the model), `ROTATION` in radians, or `SCALE`, added to the part's scale of 1. Its `keys`, in time order, give the
value reached at `time` seconds (`pre`), and, for a jump, the value leaving it (`post`, `pre` if left out); the curve
to a key is linear, or Catmull-Rom with `cubic`. Like vanilla's, channels add to the part's pose, and time is
sampled to the millisecond.

A mod's dimension draws its sky, clouds and fog from its dimension type's data (see
[Biomes, dimension types and damage types](#biomes-dimension-types-and-damage-types)), like vanilla's dimensions do on
any client: the Obsidian client reads the same fields, never the dimension's name. `skybox` picks the sky:
`overworld` (the sky disc, sun, moon, stars and sunrise), `end` (the End's sky box, with its flashes and the End's
light tint) or `none`. `timelines` set to `#minecraft:in_overworld` (or another tag holding `minecraft:day`) moves
the sun and tints the sky with the time of day. The `minecraft:visual/` attributes of the type, and of its biomes
where they're positional, set the colours and distances: `sky_color`, `fog_color`, `fog_start_distance`,
`fog_end_distance`, `sky_fog_end_distance`, `cloud_color` (clouds show only where its alpha isn't 0, which is the
default) and `cloud_height` (192.33 by default). For example, a dimension with End skies and low pink clouds:

```json
{
  "skybox": "end",
  "has_skylight": true,
  "has_ceiling": false,
  "coordinate_scale": 1,
  "min_y": 0,
  "height": 256,
  "logical_height": 256,
  "infiniburn": "#minecraft:infiniburn_end",
  "ambient_light": 0.1,
  "monster_spawn_light_level": 0,
  "monster_spawn_block_light_limit": 0,
  "attributes": {
    "minecraft:visual/fog_color": "#181318",
    "minecraft:visual/cloud_color": "#ccff99cc",
    "minecraft:visual/cloud_height": 96
  }
}
```

`IClientRegistry.RegisterDimensionEffects(string dimensionType, DimensionEffectsProperties effects)` adds what that
format can't say, for a dimension type in the mod's namespace, like NeoForge's dimension special effects:
`CloudTexture` draws its clouds from the mod's texture instead of vanilla's `clouds.png` (each pixel that isn't
transparent a 12-block cell in its colour), and `Effects` is an `IDimensionEffects` whose code runs each frame on
the game thread. Its `Adjust` gets the frame's `DimensionAtmosphere` as the data made it (sky and fog colours, fog
start and end, cloud colour and height, with the time of day and the camera for reading) and returns it changed;
the player's view (water, lava, blindness) still applies afterwards. Its `RenderSky` draws quads into the sky after
the skybox, plain or with one of the textures `SkyTextures` declares, blended or additive like the sun; a type
with `"skybox": "none"` leaves the whole sky to it. A texture without its file draws as vanilla's clouds or as plain
colour. If the code throws, the error is logged and the effects aren't called again until the mods reload, so the
dimension is drawn from its data alone.

```csharp
registry.RegisterDimensionEffects("ruby:skylands", new DimensionEffectsProperties
{
    CloudTexture = "ruby:environment/skylands_clouds",
    SkyTextures = ["ruby:environment/twin_sun"],
    Effects = new SkylandsEffects()
});

public sealed class SkylandsEffects : IDimensionEffects
{
    // Fog closes in below the islands.
    public DimensionAtmosphere Adjust(DimensionAtmosphere atmosphere) => atmosphere.Camera.Y < 32
        ? atmosphere with { FogEnd = Math.Min(atmosphere.FogEnd, 48) }
        : atmosphere;

    // A second sun, fixed in the east, 100 blocks away like vanilla's.
    public void RenderSky(DimensionAtmosphere atmosphere, IDimensionSkyBuffer buffer) =>
        buffer.Quad(new(100, 20, -10), new(100, 0, -10), new(100, 0, 10), new(100, 20, 10), Vector4.One,
            "ruby:environment/twin_sun", additive: true);
}
```

The title screen's Reload Mods button reloads the client's mods without restarting: it unloads them and loads the
`mods` folder again, then the assets. It's only on the title screen, so mods never reload while the client is
connected to a server or running a world, whose channels and ids must stay as agreed. Unloading takes back
everything mods registered in `ConfigureClient` (payload and tick handlers, key bindings, tints, renderers and what
they cached for entities, item model properties and item renderers, particle providers, dimension effects, menu
screens, HUD overlays), closes their open screens and drops their payload channels, disposes their plugins and
unloads their code; loading runs `ConfigureRegistry` and `ConfigureClient` again on new instances. Like a server's
plugin reload, content (blocks, items, entity types, particle types, menu types) stays registered for the process
with its ids, so a reload can change what mods do and how they draw, but content a mod adds needs a restart. Keys
players bound mods' bindings to stay in `options.txt`.

### Screens in code

```csharp
// The crusher's screen (see Machine menus): its progress, a button pausing it, and the ores it takes, searched and
// scrolled.
public sealed class CrusherScreen : ModMenuScreen
{
    private static readonly Item[] ores = [ItemsRegistry.IronOre, ItemsRegistry.GoldOre, ItemsRegistry.CopperOre];

    private readonly ProgressBar progress = new()
    {
        X = 79,
        Y = 34,
        Width = 24,
        Height = 16,
        Source = GuiImage.Sprite("ruby:crusher/progress")
    };

    private readonly ItemGrid list = new()
    {
        X = 110,
        Y = 30,
        Columns = 3,
        Rows = 2
    };

    private readonly ScrollBar scroll = new()
    {
        X = 166,
        Y = 30,
        Height = 34
    };

    private bool paused;

    public CrusherScreen(IScreenMenu menu)
        : base(menu)
    {
        // Init runs each time the screen is shown, so the widgets kept across showings subscribe once, here.
        this.scroll.Scrolled += row => this.list.FirstRow = row;
    }

    protected override void Init()
    {
        this.Background = GuiImage.Texture("ruby:gui/container/crusher");
        this.Add(new SlotGrid
        {
            X = 26,
            Y = 35,
            Columns = 3
        });
        this.Add(this.progress);

        var pause = this.Add(new Button
        {
            X = 8,
            Y = 58,
            Width = 50,
            Text = this.Translate("ruby.crusher.pause")
        });
        pause.Pressed += () =>
        {
            // The menu's ClickButton pauses on 0 and runs on anything else.
            this.paused = !this.paused;
            this.Menu.ClickButton(this.paused ? 0 : 1);
            pause.Text = this.Translate(this.paused ? "ruby.crusher.run" : "ruby.crusher.pause");
        };

        var search = this.Add(new TextField
        {
            X = 110,
            Y = 18,
            Width = 54,
            Height = 10,
            Bordered = false
        });
        search.Changed += this.Search;

        this.scroll.WheelArea = this.list;
        this.Add(this.list);
        this.Add(this.scroll);
        this.Search(string.Empty);
    }

    protected override void Tick() => this.progress.Progress = this.Menu.GetData(0) / 200f;

    private void Search(string text)
    {
        this.list.Items = [.. ores.Where(ore => ore.UnlocalizedName.Contains(text)).Select(ore => new ItemStack(ore))];
        this.scroll.MaxRow = (this.list.Items.Count + 2) / 3 - this.list.Rows;
        this.list.FirstRow = this.scroll.Row;
    }
}

registry.RegisterMenuScreen("ruby:crusher", menu => new CrusherScreen(menu));
```

A mod builds screens in C# from widgets in `Obsidian.API.Client.Screens`, like a NeoForge mod's `Screen` and
`AbstractContainerScreen`, and the client draws them with its own widgets, like vanilla's. A screen is a panel of
`Width` by `Height` GUI pixels (176 by 166 unless set) centred on the darkened game, with an optional `Background`, a
`GuiImage`: a GUI sprite (`GuiImage.Sprite("ruby:crusher/progress")` for
`assets/ruby/textures/gui/sprites/crusher/progress.png`, stretched, tiled or nine-sliced by its `.mcmeta` like
vanilla's) or a region of a whole texture (`GuiImage.Texture("ruby:gui/container/crusher", u, v)`, like vanilla's
container textures). Widgets are placed in the panel's pixels from its top left, and drawn in the order they were added.

The client calls the screen like vanilla's: `Init` as it opens, after removing its widgets, so it adds them there
(each time it's shown); `Tick` 20 times a second while it's open, to update the widgets from what changed; and
`Removed` as it closes. Widgets are plain objects the screen keeps and changes whenever it likes: the client draws
them as they are each frame. All of it, the widgets' events included, runs on the game's thread inside a call into the
mod; a screen that throws is logged and closed. Item grid collections are copied inside that call before rendering
reads them. When the mod unloads, its open and detached screens close and its navigation history entries are removed.

- **`Label`**: a line of text, dark grey without a shadow like container labels unless its `Color` and `Shadow` say
  otherwise, centred in its `Width` when `Centered`.
- **`Image`**: a `GuiImage` at the widget's size.
- **`ProgressBar`**: a `GuiImage` cut to its `Progress` (0 to 1, rounded up to whole pixels like a furnace's arrow),
  filling in its `Direction` (left to right, or bottom to top like a furnace's flame, ...).
- **`Button`**: vanilla's button with its `Text`, 200 by 20 unless sized; `Pressed` is raised on a click, or Enter or
  Space while it's focused, if it's `Active`.
- **`TextField`**: vanilla's edit box, with its caret, selection and the clipboard; `Changed` is raised with the new
  `Text` as the player types (up to `MaxLength`). `Hint` shows while it's empty, and `Bordered = false` draws only the
  text, like the creative inventory's search.
- **`ScrollBar`**: a vertical bar the player drags, or scrolls with the wheel over it or its `WheelArea`, from row 0 to
  `MaxRow`; `Scrolled` is raised with the new `Row`. Its handle is vanilla's creative inventory scroller unless
  `Handle` names another sprite (with a `_disabled` one, drawn while `MaxRow` is 0).
- **`SlotGrid`**: the menu's slots from `FirstSlot`, `Columns` by `Rows`, 18 pixels apart from where its first item
  goes, like `MenuScreenProperties.Slots`. The player clicks, drags, shift-clicks and swaps them with hotbar keys like
  any container's, and the client draws their stacks and tooltips.
- **`ItemGrid`**: stacks that aren't the menu's slots, like a storage terminal's contents or a list of recipes:
  `Items` row by row, `Columns` by `Rows` from `FirstRow`, with their tooltips. `Clicked` is raised with the cell's
  index (even past the list's end), the mouse button (0 left, 1 right, 2 middle, like vanilla's) and Shift; the mod
  decides what it does, typically with a payload or a menu button.
- **Tooltips**: any widget's `Tooltip` text shows while the mouse is over it, a line per `\n`, with vanilla's legacy
  `§` formatting codes.

A `ModMenuScreen` is the screen of a menu type, registered with `IClientRegistry.RegisterMenuScreen(menuType,
factory)`: the client makes one from the factory each time the server opens such a menu, and shows it in a container
screen, with the menu's title at `Title`, the player's inventory at `Inventory` (three rows and the hotbar, like
vanilla's menus; null to place those slots with `SlotGrid`s, which follow the menu's own from `Menu.SlotCount`, or to
leave them out) and its "Inventory" label 12 pixels above it unless `InventoryLabel` says otherwise. Its `Menu` is the
client's side of the menu: its `Type`, `Title`, `SlotCount`, isolated copies of its slot stacks (`GetItem`, retaining
their count and components), its data slots' values as last sent (`GetData`, 0 until sent), the extra data it was
opened with (`ReadOpenData()`, a new reader each call, reading what `WriteOpenData` wrote) and `ClickButton(number)`,
which the server's `ClickButton` gets. Escape or the inventory key close the screen and the menu, as does `Close()`.
The [engine sample](../samples/EngineSample)'s grinder is a complete machine menu and screen.

A mod's own screens, like settings, aren't tied to a menu: subclass `ModScreen` and open it with
`registry.Screens.Open(screen)`, from a key binding's `Pressed` or a payload handler (from another thread it opens on
the game's shortly after). It shows over the game, or over the screen shown, which Escape or `Close()` goes back to.
Like `Connection`, `registry.Screens` stays usable after `ConfigureClient`.

### Screens in Razor

```razor
@* Crusher.razor: the crusher's screen of Screens in code, built in Razor. *@
<SlotGrid X="26" Y="35" Columns="3" />
<ProgressBar X="79" Y="34" Width="24" Height="16" Source="Arrow" Progress="@(this.Menu.GetData(0) / 200f)" />
<Button X="8"
        Y="58"
        Width="50"
        Text="@this.Screen.Translate(this.paused ? "ruby.crusher.run" : "ruby.crusher.pause")"
        OnClick="this.TogglePause" />
<TextField X="110" Y="18" Width="54" Height="10" Bordered="false" Value="@this.search" ValueChanged="this.Search" />
<ItemGrid X="110" Y="30" Columns="3" Rows="2" Items="this.shown" @bind-FirstRow="this.row" />
<ScrollBar X="166" Y="30" Height="34" MaxRow="@((this.shown.Length + 2) / 3 - 2)" @bind-Row="this.row" />

@code {
    private static readonly GuiImage Arrow = GuiImage.Sprite("ruby:crusher/progress");
    private static readonly Item[] ores = [ItemsRegistry.IronOre, ItemsRegistry.GoldOre, ItemsRegistry.CopperOre];

    private ItemStack[] shown = [];
    private string search = string.Empty;
    private int row;
    private bool paused;

    // The client's cascading value: the screen's panel and its menu.
    [CascadingParameter]
    public ScreenContext Screen { get; set; } = null!;

    private IScreenMenu Menu => this.Screen.Menu!;

    protected override void OnInitialized()
    {
        this.Screen.Background = GuiImage.Texture("ruby:gui/container/crusher");
        this.Search(string.Empty);
    }

    private void TogglePause()
    {
        // The menu's ClickButton pauses on 0 and runs on anything else.
        this.paused = !this.paused;
        this.Menu.ClickButton(this.paused ? 0 : 1);
    }

    private void Search(string text)
    {
        this.search = text;
        this.shown = [.. ores.Where(ore => ore.UnlocalizedName.Contains(text)).Select(ore => new ItemStack(ore))];
        this.row = 0;
    }
}
```

```csharp
registry.RegisterMenuScreen<Crusher>("ruby:crusher");
```

A mod can build its screens in Razor rather than in code: `.razor` components, which the Razor SDK compiles into the
mod's assembly when the mod builds, so the client loads and unloads them with the rest of the mod's code. The mod's
project uses the Razor SDK, and its components inherit `ModComponent` (set once in `_Imports.razor`):

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- A mod has no web assets: its output is what its archive holds. -->
    <StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>
  </PropertyGroup>
  <ItemGroup>
    <!-- The server or client provides the API, so it isn't packed with the mod. Obsidian.API.Client brings the
         ASP.NET Core framework, whose Blazor components Razor screens are built on. -->
    <PackageReference Include="Obsidian.API" Version="1.0.0" ExcludeAssets="runtime" />
    <PackageReference Include="Obsidian.API.Client" Version="1.0.0" ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
```

```razor
@* _Imports.razor *@
@using Microsoft.AspNetCore.Components
@using Obsidian.API
@using Obsidian.API.Client.Razor
@using Obsidian.API.Client.Screens
@using Obsidian.API.Inventory
@using Obsidian.API.Registries
@inherits ModComponent
```

A screen in Razor is the same screen as one in code, hosted the same way: `RegisterMenuScreen<TComponent>(menuType)`
shows a menu type with a new `TComponent` each time the server opens the menu, in its container screen with the
menu's title, slots and the player's inventory, and `registry.Screens.Open<TComponent>(parameters)` opens a mod's own
screen, given the parameters by name. The component draws with the building blocks of `Obsidian.API.Client.Razor`,
the Razor counterparts of the widgets in [Screens in code](#screens-in-code), which the client draws with its own
widgets; HTML elements draw nothing.

- **`Panel`**: a box holding parts, its child content, at their positions or stacked by its `Flow` (`Column` or
  `Row`, with `Gap`, `Padding` and `Align`), optionally filled with a `Color` and clipping what's outside it (`Clip`).
- **`Label`**, **`Image`**, **`ProgressBar`**: like the widgets, with a `Source` `GuiImage` at the part's size.
- **`Button`**: `Text`, `Active`, and `OnClick`.
- **`TextField`**: `Value` and `ValueChanged` (`@bind-Value`), `MaxLength`, `Hint` and `Bordered`.
- **`ScrollBar`**: `Row` and `RowChanged` (`@bind-Row`) up to `MaxRow`, with its `Handle` sprite; the wheel over it
  scrolls it.
- **`SlotGrid`**: the menu's slots from `FirstSlot`, `Columns` by `Rows`. Its position is from the top left of the
  screen's panel wherever it is in the markup, since the container screen draws the slots.
- **`ItemGrid`**: `Items`, `Columns` by `Rows` from `FirstRow`, with their tooltips; `OnClick` with the cell, the
  button and Shift, and the wheel over it raises `FirstRowChanged` (`@bind-FirstRow`, to the same field as a scroll
  bar's `@bind-Row`).
- **Tooltips**: any part's `Tooltip` text, like a widget's.

Each part has `X`, `Y`, `Width` and `Height` in GUI pixels from the top left of the panel it's in (or offsets in a
stacking panel), with null sizes meaning its natural size or its type's default (buttons and text fields 200 by 20,
scroll bars 12 wide), and `Visible`. A mod's own components, built from parts, nest like any Razor components.

The screen's components get its `ScreenContext` as a cascading value: the `Menu` (the same `IScreenMenu` as a
`ModMenuScreen`'s; null for a mod's own screen), the panel's `Width`, `Height` and `Background`, for a menu's screen
its `Inventory`, `Title` and `InventoryLabel` positions, `Close()`, `Translate` and `TextWidth`. Set the panel's
properties as the component initializes; the client lays the screen out again after its first render. The client
redraws the screen every client tick, and components taking the context render again with it, so markup reading the
menu's data slots shows them as last sent; others render when their parameters change, after their event handlers,
and on `StateHasChanged`.

`ModComponent` is the mod's counterpart of Blazor's `ComponentBase`, with its lifecycle (`OnInitialized(Async)`,
`OnParametersSet(Async)`, `OnAfterRender(Async)`, `ShouldRender`, `StateHasChanged`, `InvokeAsync`), and
`OnDispose` to clean up. The client runs all of it, rendering and event handlers included, on the game's thread
inside a call into the mod, as it does a screen in code, and reads the mod's objects the parts are given (child
content, item lists) and raises their events inside that call too; an async method stays inside it until its task
completes. Whatever throws is logged and closes the screen. For that, the client only renders a mod's components that
inherit `ModComponent` and don't implement Blazor's component interfaces (`IComponent`, `IHandleEvent`,
`IHandleAfterRender`, `IDisposable`, `IAsyncDisposable`) themselves. When the mod unloads, its screens close and
their navigation history goes, like screens in code, and the client forgets its component types in Blazor's caches,
so that its code unloads. The [engine sample](../samples/EngineSample)'s grinder registers `GrinderView.razor`, the
Razor form of its `GrinderScreen`.

### HUD overlays

```csharp
// The player's mana in the top left corner, a bar and its amount, from what the server last sent.
public sealed class ManaOverlay : ModOverlay
{
    private readonly ProgressBar bar = new()
    {
        Y = 10,
        Width = 81,
        Height = 5,
        Source = GuiImage.Sprite("ruby:hud/mana_bar")
    };

    private readonly Label amount = new()
    {
        Color = 0xFF55FFFF,
        Shadow = true
    };

    public ManaOverlay()
    {
        this.X = 4;
        this.Y = 4;
        this.Width = 81;
        this.Height = 15;
        this.Add(new Image
        {
            Y = 10,
            Width = 81,
            Height = 5,
            Source = GuiImage.Sprite("ruby:hud/mana_bar_background")
        });
        this.Add(this.bar);
        this.Add(this.amount);
    }

    // Set by the payload handler, on the connection's thread; the widgets change on the game's, in Tick.
    public int Mana { get; set; }

    protected override void Tick()
    {
        var mana = this.Mana;
        this.bar.Progress = mana / 20f;
        this.amount.Text = $"{mana} / 20";
    }
}

var mana = new ManaOverlay();
registry.RegisterOverlay(mana);
registry.HandlePayload<ManaChanged>((changed, _) => mana.Mana = changed.Mana);
```

A mod draws on the HUD with a `ModOverlay`, registered with `IClientRegistry.RegisterOverlay(overlay)`, like a
NeoForge mod's GUI layer: widgets from [Screens in code](#screens-in-code) in a box of `Width` by `Height` GUI pixels.
The box is placed at its `Anchor`, a corner of the screen, the middle of an edge or the centre, which is the same
point of the box (`TopRight` puts the box's top right corner in the screen's; `TopLeft` is the default), then moved
`X` pixels right and `Y` down (negative for left and up). Its widgets are placed from its top left. Overlays only
show: they draw labels, images, progress bars and item grids (without tooltips), and leave out buttons, text fields,
scroll bars and slot grids, since the HUD takes no input.

`Layer` says which of vanilla's HUD the overlay goes over, like the vanilla layer a NeoForge mod registers its layer
below or above: `BelowChat`, the default, draws it over the hotbar, the player's bars, boss bars, the scoreboard,
titles and the debug text, and under the chat and the player list; `AboveAll` over all of them. Overlays in a layer
are drawn in the order they were registered.

Overlays are part of the HUD: they're drawn while the player is in a world, under any screen like vanilla's HUD, and
not while the player hides the HUD (F1). Their `Tick` runs on the game's thread every client tick while the player is
in a world, under screens too, inside a call into the mod, to update the widgets, which are drawn as they are each
frame; an overlay whose `Tick` throws is logged and removed. A mod's overlays go when it unloads, and its
`ConfigureClient` registers them again when it reloads.
