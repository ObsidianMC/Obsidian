# Plugin blocks, entities, tags and data

Plugins add blocks in code or from JSON files that Obsidian's source generator turns into code, add entity types,
worldgen types and level generators in code, and add tags, biomes, dimension types, damage types, worldgen features,
biome modifiers, noise settings and dimensions with data pack files, much like NeoForge and Fabric mods.
Clients need the matching mod for blocks, items and entity types: NeoForge and Fabric clients are sent those
registries, so their ids agree with the server's. Biomes, dimension types and damage types are sent with their data,
so vanilla clients can join too. On the Obsidian client, a mod adds its client side in code (see [Client](#client)).

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
places the block and is what it drops. A client mod has to declare the same block with the same properties and values
in the same order: bool properties are `true, false`, int ones ascending, enum ones in the order given.

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
`requiresCorrectTool`, `lightEmission`), `behavior` is an `IBlockBehavior` type's full name, and `item` is false for a
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
they fall and rest where they land. Every kind calls the type's behaviour each tick and when a player uses it. The
client mod's entity should be of the same kind, so that their data matches. Entities are saved with the type's
identifier; while the plugin is gone, saved ones stay as they were.

A mob's `Category` (a `MobCategory`, `Misc` by default) is its vanilla mob category. Biomes' `spawners` and biome
modifiers adding spawners (see [Biome modifiers](#biome-modifiers)) spawn a plugin's mobs naturally while the
plugin is loaded, and they count against their category's cap; misc mobs don't spawn naturally, since plugin mobs
don't despawn. They spawn with the rules of the list they're chosen from: on grass in light as creatures, in water in
the water categories, and in the dark otherwise.

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
Two kinds of attribute value can't be read yet, so a file with them is left out:

- particles that take options, like `minecraft:dust` or `minecraft:block` (ones without options, like
  `minecraft:white_ash`, are fine);
- a bed rule's `error_message` other than a string, `{"text": ...}` or `{"translate": ..., "fallback": ...}`.

The server's own gameplay doesn't apply attributes yet: they change what clients show and play.

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
`overlay_processors`: vanilla's lists by identifier, or lists defined inline, as `{"processors": [...]}` or a bare
list. The `StructureProcessor` base class is in `Obsidian.API.World.Structures`, so a plugin can add processor types,
which data names by their `processor_type`, and give its feature types properties of them:

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
Processors are shared between threads, so keep them stateless. Plugins can't add `worldgen/processor_list` files
yet. Vanilla's own processors stay in Obsidian.

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
constant), an identifier, or an object with a `type`; a noise is always named by its identifier, since noises are
seeded from it. Plugins' dimensions use plugins' noise settings (see [Dimensions](#dimensions)). Ground rolling
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
- Not supported: noises defined inline (vanilla can't seed them either), a constant number for a spline function's
  `spline`, and a range for a spawn target's `depth`. Noise settings may be defined inline in a level stem (see
  [Dimensions](#dimensions)).

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
dimension `<namespace>:<path>`. The server's default world gets a level of that name, generated by vanilla's noise
generator, so plugin biomes generate somewhere. A dimension with one plugin biome everywhere:

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

- `type` names a dimension type, vanilla's or a plugin's. It decides the level's build range, sky light and
  everything else a dimension type does, and clients are sent it like any dimension type.
- `generator` is `minecraft:noise`, with `settings` naming vanilla's noise settings (`minecraft:overworld`,
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
- Biomes are vanilla's or plugins'. Dimension types and biomes are named: defining them inline isn't supported yet,
  since clients are sent them by name.
- Not supported yet, and rejected with a message saying so: `minecraft:flat` and `minecraft:debug` generators, and
  noise settings that generate outside the dimension type's build range (vanilla clamps them; its own pairings all
  fit). Generation fills whole cells of `size_vertical * 4` blocks from the cell holding `min_y`, so a `min_y` that
  isn't a multiple of that fills from below it: the overworld's `min_y` -64 with `size_vertical` 3 fills from -72,
  outside the overworld's build range.

The dimension generates like a vanilla dimension with those settings: terrain, surface, aquifers and ore veins come
from the noise settings, caves and canyons are carved like in the vanilla dimension using the same noise settings
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
  logged and left out.
- A file's path gives the dimension's identifier, and, as vanilla checks resource paths, one with an empty, `.` or
  `..` segment (like `data/ruby/dimension//world.json`) is refused. This holds for every plugin data file.
- A dimension's level is created when its plugin loads, at startup or later, and is saved in the world's folder under
  `dimensions/<namespace>/<path>`, where vanilla saves a data pack's dimensions, and loaded from there after a restart.
  Players can reach it, and it ticks, once it's loaded or generated with its spawn. One that fails to load or generate
  is logged and left out, and the next plugin load tries it again.
- Gameplay follows the dimension type, as in vanilla: monsters spawn with its monster light limits, and undead mobs
  burn by day in a type with the overworld's timelines (`#minecraft:in_overworld`) or its own
  `minecraft:gameplay/monsters_burn`. Patrols, wandering traders and phantom spawns stay in the overworld, where
  vanilla runs them; raids, the end fight and portals stay with vanilla's levels for now.
- A dimension's level stays until the server stops, even when its plugin unloads, with the noise settings it was
  created with. Its features come and go with their plugins like any features.
- A reload changing a level stem, or the plugin data it reads (noise settings, density functions and noises, its
  biome source's included, and the biomes it names), keeps the dimension as it was, with a warning: the change
  applies after a restart, since changing a live level's generator would mix two generators' chunks. If the
  dimension stops meanwhile, it stays stopped until then.
- A dimension generating with a plugin's types stops generating new chunks when that plugin unloads, and generates
  again when it loads again (see [Noise types](#noise-types)); its generated chunks still load.
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
  biomes, dimension and damage types, features, carvers, noise settings, density functions, noises, parameter lists,
  level stems and tags (blocks, items, entity types, biomes, placed features and configured carvers). Other files, like
  recipes, loot tables or structures, are left out unread, and how many is logged. A pack's `filter` and `overlays`
  aren't applied, which is logged once for the pack. Data packs are read once: a change applies after a restart.
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
  vanilla's files with the runtime reader, so they use it, as vanilla's do by reference. Saplings, bone meal and
  structures still use vanilla's own features.
- noise settings, density functions and noises, read again the same way through vanilla's entries naming them. The
  noises the surface builder reads by name (like `minecraft:surface` or the badlands' and icebergs' noises) stay
  vanilla's.
- parameter lists: `data/minecraft/worldgen/multi_noise_biome_source_parameter_list/overworld.json` with
  `{"preset": "minecraft:nether"}` makes the overworld pick nether biomes from its climate.
- level stems: `data/minecraft/dimension/overworld.json` (or `the_nether`, `the_end`) replaces how that dimension
  generates, read like any [dimension](#dimensions), with an inline `biomes` list, say. It must keep vanilla's
  dimension type (`minecraft:overworld`, `minecraft:the_nether`, `minecraft:the_end`), since Obsidian creates
  vanilla's levels with those; override the type's file to change it. A type's build range (`min_y` and `height`)
  must hold the noise settings its vanilla dimension generates with, as overridden: vanilla clamps generation to the
  build range, which Obsidian can't yet, so a type override that doesn't fit, like the overworld's at `min_y` 0 with
  vanilla's overworld noise settings from -64, is logged once startup has read every plugin and pack, and left out,
  keeping vanilla's type. Override the noise settings to match it. A stem startup couldn't read yet, like a
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
  never replaced. `biomes` is a parameter list, the dimension's whole layout where the region is: every climate
  should find a fitting biome, oceans and caves included. A region has one of the two.
- Biomes are vanilla's or plugins' registered ones, the plugin's own or those of plugins loaded before it. Fields the
  format doesn't have are errors. A file that can't be read is logged and left out, and the plugin loads without it.
  When two plugins define the same region, the last in load order wins, as for other entries.

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
  the regions of a dimension that generates already; the change is logged, and applies after a restart.
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
Plugins' level stems can't name a plugin generator as their `generator` yet: they're generated with vanilla's noise
generator (see [Dimensions](#dimensions)).

The rules:

- The identifier is in the plugin's namespace, and registering is only possible in `ConfigureRegistry`. Identifiers
  the host registered (`overworld`, `superflat`, ...) are the host's.
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
(see [mod-system.md](mod-system.md)). Its plugin class implements `IClientPlugin`, from `Obsidian.API.Client`, to
register its client side. The client calls `ConfigureClient` once at startup, after `ConfigureRegistry`; servers never
call it, so a `both` mod runs on a server without any client code.

What `ConfigureClient` registers takes effect when it returns and stays until the client exits; if it throws, the
mod fails to load and none of it does. The registry only takes registrations during `ConfigureClient`, since the
client loads the assets that depend on them afterwards: registering later, through a registry the mod kept, throws
`InvalidOperationException`. Its `Connection`, and sending payloads on it, stay usable for as long as the client
runs. Like the server's content, key bindings, tints and renderers are the mod's own: their identifiers are in its
namespace (its mod id) and valid by vanilla's rules, tints and renderers are for blocks and entity types it
registered, and registering one twice throws `ArgumentException`.

```csharp
public sealed class RubyMod : PluginBase, IClientPlugin
{
    public override void ConfigureRegistry(IPluginRegistry registry)
    {
        registry.RegisterBlock("ruby:mossy_ruby");
        registry.RegisterEntity("ruby:golem", new EntityProperties { Width = 1.4f, Height = 2.7f });
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

        registry.RegisterBlockTint("ruby:mossy_ruby", BlockTint.Foliage);
        registry.RegisterEntityRenderer("ruby:golem", new EntityRendererProperties
        {
            Model = "ruby:golem",
            Texture = "ruby:entity/golem",
            ShadowRadius = 0.7f
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
- **Block tints.** A tinted block's faces whose model gives them a `tintindex` are multiplied by the tint, like
  vanilla's block colours: the biome's grass, foliage, dry foliage or water colour, blended across nearby biomes with
  the player's biome blend (grass follows the swamp and dark forest modifiers), or `BlockTint.Fixed(0xRRGGBB)`. Its
  item is tinted too, on every `tintindex`, unless its item definition gives the model `tints` of its own (which
  then apply by index): with vanilla's grass block item colour (the grass colormap at temperature 0.5 and downfall
  1), its default foliage colour (`#48B518`), dry foliage colour (`#5C3C32`) or water colour (`#3F76E4`), or the
  fixed colour.
- **Entity renderers.** A mod's entity type is drawn with a model layer from `assets/<ns>/models/entity/<path>.json`
  (`Model = "<ns>:<path>"`) and a texture from `assets/<ns>/textures/entity/...` (`Texture = "<ns>:entity/..."`, or
  `minecraft:entity/...` for vanilla's), scaled by `Scale`, with a shadow of `ShadowRadius` blocks. Without a
  renderer, or with a model that's missing or doesn't parse (which is logged), its entities aren't drawn. Entities
  are placed like vanilla's living entities: turned to their body's yaw and tilted as they die or sleep. Parts named
  `head` turn to where the entity looks, and parts whose names end in `leg` swing as it walks (left and right, and
  `front` and back, in opposite phases); other parts stay as modelled. Vanilla's rules hiding parts by name, like
  `left_chest` without a chest, don't apply.

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

Particle providers and screens for menu types are for later: plugins can't register particle types or menu types on
the server yet.
