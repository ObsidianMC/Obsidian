# Plugin blocks, entities, tags and data

Plugins add blocks in code or from JSON files that Obsidian's source generator turns into code, add entity types,
worldgen types and level generators in code, and add tags, biomes, dimension types, damage types, worldgen features,
biome modifiers, noise settings and dimensions with data pack files, much like NeoForge and Fabric mods.
Clients need the matching mod for blocks, items and entity types: NeoForge and Fabric clients are sent those
registries, so their ids agree with the server's. Biomes, dimension types and damage types are sent with their data,
so vanilla clients can join too.

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
tags with the loaded plugins' files in load order whenever a plugin loads or unloads, and vanilla's `Tag` objects hold
the result, so server code using them sees the plugins' entries. `IPluginRegistry.AddToTag` adds entries from code.

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
- Plugins can't override vanilla's entries: a file in the `minecraft` namespace is left out, with a warning. When two
  plugins define the same entry, the first in load order wins and the other is logged.
- A file vanilla wouldn't accept is logged and left out, and the plugin loads without it (see below).
- A dimension type keeps its `min_y`, `height` and `logical_height` until the server restarts: a reload changing them
  keeps the entry as it was, with a warning.

Clients are sent a plugin's entries as vanilla's network codecs write the values read from its files, not the files
themselves, and vanilla's entries by name, so vanilla clients always decode them like a data pack's entries. A colour
given as an int is sent as `"#rrggbb"`, defaults vanilla leaves out are left out, and of a biome only its climate,
attributes and effects are sent, as vanilla sends them. Players already connected go back through configuration when
entries are added or their data changes. Plugin biomes generate in plugins' dimensions (see [Dimensions](#dimensions)),
not in vanilla's yet, and the Obsidian client colours biomes from compiled tables, so plugin biomes have no tint there
yet.

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

- Like other plugin data, files in the `minecraft` namespace are left out, and when two plugins define the same entry,
  the first in load order wins. A file that can't be read is logged and left out, with the features and modifiers
  that need it; the plugin loads without them. Values are checked like the source generator checks vanilla's:
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
- `data/<namespace>/worldgen/noise/<path>.json` is a noise, `{"firstOctave": ..., "amplitudes": [...]}`.

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

- Like other plugin data, files in the `minecraft` namespace are left out, and when two plugins define the same entry,
  the first in load order wins. A file that can't be read is logged and left out, with the entries that need it; the
  plugin loads without them. Unknown types, fields and blocks, missing fields and values out of vanilla's ranges are
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
    (`minecraft:overworld` or `minecraft:nether`, or inline as `{"preset": "minecraft:overworld"}`). Each parameter
    (`temperature`, `humidity`, `continentalness`, `erosion`, `depth`, `weirdness`) is a number from -2 to 2 or a
    range, `[min, max]` or `{"min": ..., "max": ...}`, and `offset` is from 0 to 1. Biomes are picked from the climate
    like vanilla picks them;
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
  fit).

The dimension generates like a vanilla dimension with those settings: terrain, surface, aquifers and ore veins come
from the noise settings, caves and canyons are carved like in the vanilla dimension using the same noise settings
(like the overworld with a plugin's noise settings), features are the dimension's biomes' (see
[World generation features](#world-generation-features)), and structures generate where their biomes allow, as in
vanilla. Players are placed on the ground near the climate's spawn, like the overworld's spawn.

The rules:

- Like other plugin data, files in the `minecraft` namespace are left out, and when two plugins define the same
  dimension, the first in load order wins. A file that can't be read is logged and left out; the plugin loads without
  it. A dimension whose biomes' features can't be ordered (vanilla can't generate it either) is logged and left out.
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
