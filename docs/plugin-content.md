# Plugin blocks, entities, tags and data

Plugins add blocks in code or from JSON files that Obsidian's source generator turns into code, add block entity
types, entity types, particle types, menu types, worldgen types and level generators in code, and add tags, biomes,
dimension types, damage types, worldgen features, biome modifiers, noise settings, loot tables and dimensions with data
pack files, like NeoForge and Fabric mods. Clients need the matching mod for blocks, block entity types, items, entity
types, particle types and menus: NeoForge and Fabric clients, and the Obsidian client, receive those registries and map
the server's ids to their own. The mods may load in any order. Biomes, dimension types and damage types are sent with
their data, so vanilla clients can join too. On the Obsidian client, a mod adds its client
side in code (see [Client](#client)).

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
first `container` item and queue their initial client data. A failed shulker loot evaluation restores retirement
while the box is still standing; malformed `limit_count` bounds are rejected when the table is loaded.
Banner patterns, bees and skull profiles aren't taken yet, and neither is a plugin's block entity data: keeping it on
the dropped item waits for plugin data component types.

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
- `Equippable`: a player wears the item in its slot by using it, or by placing it in the armour slot.
  `ComponentBuilder.Equippable` has vanilla's defaults; set the slot.
- `AttributeModifiers`: what the item adds while it's in the modifier's slot, by attribute id (`AttributeIds`).
  Obsidian applies armour, armour toughness and knockback resistance from worn items and attack damage and speed
  from held ones; modifiers of other attributes reach clients but don't change the player.

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
  `ConfigureClient`; `Level` is null there, and `SetChanged` and `SendUpdate` do nothing. The instance is dropped
  (`IsRemoved`) when its block changes to one without its type, its chunk unloads, the player leaves the server or
  the mod unloads. A tick or load that throws is logged, and the instance isn't ticked again until the next update.
  Shapes and rendering keep reading the snapshot, from their own threads, never the live instance.
- **Threads.** The server calls `Load`, `Save`, `TickAsync`, `GetUpdateData` and `OnRemoved` on the level's tick,
  one at a time, inside calls into the plugin: a block removed from elsewhere (a player's break) runs `OnRemoved` on
  the next tick. Only a new block entity for a block set elsewhere is constructed there. Chunks are saved and sent
  from copies of what those calls returned last, so the block entity's code needs no locks of its own, and changing a
  compound it handed over changes nothing saved or sent.
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
inventories before their final save and close remote viewers. The server saves a plugin container's size and drop
policy as `obsidian:container` data, so its items and pending loot still have container removal semantics while the
plugin is absent. While it's gone they're kept as they were saved, in loaded chunks and in chunks loaded meanwhile,
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

## Menus

```csharp
var pouch = registry.RegisterMenu("ruby:pouch", rows: 1, columns: 5);

var container = new MenuContainer(pouch, "Ruby pouch");
container.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 3));
await player.OpenInventoryAsync(container);
```

A plugin's menu type is a grid of 1 to 6 rows of 1 to 9 slots above the player's inventory, numbered row by row and
followed by the player's inventory like vanilla's menus. The server opens it with its id and moves items in it like a
chest's; a `ContainerClickEventArgs` handler cancels or handles players' clicks, and the plugin reads and changes the
container's slots. Several players can have the same container open: their clicks take turns, so two can't take the
same stack, and each sees the others' changes. A NeoForge or Fabric mod's menu type with the same identifier and slots
shows it, made without extra data (vanilla's open screen packet carries none). The type keeps its id and shape across
reloads and stays registered until the server stops; registering it again with another shape throws. When the plugin
unloads (or reloads), its menus close for the players who have them open, with a `ContainerClosedEventArgs` the plugin
still handles although it takes no other calls. From then until a loaded plugin registers the type again, opening one
of its menus does nothing.

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
- Furnaces, blast furnaces and smokers take a smelting, blasting or smoking recipe's ingredient when it's
  shift-clicked into them. Obsidian doesn't cook yet, nor use stonecutting, smithing or campfire recipes, vanilla's
  or plugins': those are kept but unused.

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
  or `/locate poi`, and keeps no statistics, so throws don't count toward the eye's `minecraft:used`.

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
build height of the type, and its plugin biomes in chunks by their registry ids. There are no portals to plugins'
dimensions, and only the default world has them.

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
it's interfaces and data only, and servers ship it too, so the same plugin class loads on them. The client calls
`ConfigureClient` when it loads its mods, after `ConfigureRegistry`: at startup, and on a new instance of the plugin
when the player reloads them (see below); servers never call it, so a `both` mod runs on a server without any client
code.

What `ConfigureClient` registers takes effect when it returns and stays until the client exits or reloads its mods;
if it throws, the mod fails to load and none of it does. The registry only takes registrations during
`ConfigureClient`, since the client loads the assets that depend on them afterwards: registering later, through a
registry the mod kept, throws `InvalidOperationException`. Its `Connection`, and sending payloads on it, stay usable
for as long as the client runs. Like the server's content, key bindings, tints, renderers, particle providers,
block entity shape providers and menu screens are the mod's own: their identifiers are in its namespace (its mod
id) and valid by vanilla's rules, they're for content it registered, and registering one twice throws
`ArgumentException`.

`IClientRegistry.RegisterBlockEntityShapes(string blockEntityType,
Func<IReadOnlyDictionary<string, object?>, BlockShapes?> provider)` gives a mod's block entity type its collision
and outline shapes from its latest synced update data. Register the type in `ConfigureRegistry` on both sides
with `RegisterBlockEntity<T>`, then its provider in `ConfigureClient`. The provider receives plain NBT values:
numbers, strings and arrays, nested compounds as dictionaries and lists as `List<object?>`. The outer dictionary
is read-only; do not mutate nested values. Keep the provider pure and thread-safe, since it runs on a reading
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
- **Block tints.** A tinted block's faces whose model gives them a `tintindex` are multiplied by the tint, like
  vanilla's block colours: the biome's grass, foliage, dry foliage or water colour, blended across nearby biomes with
  the player's biome blend (grass follows the swamp and dark forest modifiers), or `BlockTint.Fixed(0xRRGGBB)`. Its
  item is tinted too, on every `tintindex`, unless its item definition gives the model `tints` of its own (which
  then apply by index): with vanilla's grass block item colour (the grass colormap at temperature 0.5 and downfall
  1), its default foliage colour (`#48B518`), dry foliage colour (`#5C3C32`) or water colour (`#3F76E4`), or the
  fixed colour.
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
  "Inventory" label. Its slots must be as many as the menu has.
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

The title screen's Reload Mods button reloads the client's mods without restarting: it unloads them and loads the
`mods` folder again, then the assets. It's only on the title screen, so mods never reload while the client is
connected to a server or running a world, whose channels and ids must stay as agreed. Unloading takes back
everything mods registered in `ConfigureClient` (payload and tick handlers, key bindings, tints, renderers, particle
providers, menu screens) and their payload channels, disposes their plugins and unloads their code; loading runs
`ConfigureRegistry` and `ConfigureClient` again on new instances. Like a server's plugin reload, content (blocks,
items, entity types, particle types, menu types) stays registered for the process with its ids, so a reload can
change what mods do and how they draw, but content a mod adds needs a restart. Keys players bound mods' bindings to
stay in `options.txt`.
