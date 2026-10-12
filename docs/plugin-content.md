# Plugin blocks, entities, tags and data

Plugins add blocks in code or from JSON files that Obsidian's source generator turns into code, add entity types and
worldgen feature types in code, and add tags, biomes, dimension types, damage types, worldgen features, NeoForge
biome modifiers and dimensions with data pack files, like NeoForge and Fabric mods.
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

## Tags

Tags are data pack tag files: `data/<namespace>/tags/block/<path>.json`, `tags/item/...` or `tags/entity_type/...`,
with `values` (identifiers, `#` followed by a tag, or `{ "id": ..., "required": false }`), `replace`, and NeoForge's
`remove`. A
file for a vanilla tag adds to it, for example `data/minecraft/tags/block/mineable/pickaxe.json` to mine a block with a
pickaxe; other files make new tags, like the shared `c:` ones. Obsidian merges vanilla's tags with the loaded plugins'
files in load order whenever a plugin loads or unloads, and vanilla's `Tag` objects hold the result, so server code
using them sees the plugins' entries. `IPluginRegistry.AddToTag` adds entries from code.

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
  identifier, or placed features defined inline (see [World generation features](#world-generation-features)). Its
  `carvers` refer to vanilla's configured carvers by identifier: plugins can't add carvers or define them inline
  yet. Where vanilla would log and drop a carver, feature or spawner it can't read, the file is left out instead.
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

Plugins add configured and placed features, and change which features biomes place with NeoForge's biome modifiers:

- `data/<namespace>/worldgen/configured_feature/<path>.json` is the configured feature `<namespace>:<path>`;
- `data/<namespace>/worldgen/placed_feature/<path>.json` is a placed feature;
- `data/<namespace>/neoforge/biome_modifier/<path>.json` is a biome modifier.

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
// data/ruby/neoforge/biome_modifier/add_ore_ruby.json
{
  "type": "neoforge:add_features",
  "biomes": "#minecraft:is_overworld",
  "features": "ruby:ore_ruby",
  "step": "underground_ores"
}
```

A biome modifier is NeoForge's:

- `neoforge:add_features` adds `features` to the end of a decoration `step` of each of the `biomes`;
- `neoforge:remove_features` removes `features` from the `biomes`, in the given `steps` (one or a list) or in all;
- `neoforge:none` does nothing.

`biomes` is a biome (vanilla's or a plugin's), a list of them, or a vanilla biome tag like `#minecraft:is_overworld`;
`features` is a placed feature, or a list of them, and features added may be defined inline. Steps are vanilla's
decoration steps: `raw_generation`, `lakes`, `local_modifications`, `underground_structures`, `surface_structures`,
`strongholds`, `underground_ores`, `underground_decoration`, `fluid_springs`, `vegetal_decoration` and
`top_layer_modification`. Other modifier types (spawns, carvers), NeoForge's holder set types (`neoforge:any`,
`neoforge:and`, ...) and placed feature tags aren't supported yet, and leave the file out.

The rules:

- Like other plugin data, files in the `minecraft` namespace are left out, and when two plugins define the same entry,
  the first in load order wins. A file that can't be read is logged and left out, with the features and modifiers
  that need it; the plugin loads without them. Values are checked like the source generator checks vanilla's:
  unknown types, fields, enum names and blocks, and missing required fields, are errors.
- Features may refer to each other in any order and across plugins; a feature that refers back to itself is left
  out. Each feature is read once and shared, so a feature given by identifier is the same instance everywhere.
- A biome's features are vanilla's, or a plugin biome's `features`, with every `add_features` modifier applied and
  then every `remove_features` one, each in load order, like NeoForge's phases. Features are ordered across a
  dimension's biomes like vanilla orders them, which decides their random seeds, so a world generates like vanilla
  with the same data pack. Like NeoForge, only the lists after every phase count: an add that a later removal
  resolves is fine. When a dimension's final lists would order features in a cycle (one placed both before and after
  another in its biomes, which vanilla can't generate), the error is logged and that dimension's biomes get their own
  features back, without plugins' biome modifiers. Dimensions share biomes, so this repeats until every dimension's
  features can be ordered; other dimensions keep the modifiers where they can.
- Placing a feature made of a plugin's types (its feature type, a placement modifier or a provider inside it) is a
  call into the plugin, like its event handlers, so unloading waits for chunks being decorated with it. Chunks
  decorated while it unloads leave its features out.
- Features aren't sent to clients or saved, so they come and go with their plugin: when plugins load or unload, the
  features are read again and every world's generator moves to them for the chunks it decorates next. Chunks
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
- `SimpleBlockState` (`{"Name": ..., "Properties": {...}}`) and `BlockSet` (blocks and `#` block tags);
- vanilla's kinds of worldgen values, which take any type of that kind by its `type`: `IIntProvider` (a number is a
  constant), `IFloatProvider`, `IHeightProvider` and `VerticalAnchor`, `IBlockStateProvider`, `IBlockPredicate` and
  `IRuleTest`; and `ConfiguredFeatureBase` and `PlacedFeature`, by identifier or inline;
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
- Not in the API yet: vanilla's tree parts (trunk, foliage and root placers, tree decorators), structure processors,
  weighted lists (`WeightedEntry<T>`) and biome sets. Plugin types can't have properties of those types, and plugins
  can't add placer or decorator types. Density function, surface rule and biome source types come with plugins'
  noise settings.

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
- `generator` is `minecraft:noise`, with `settings` naming one of vanilla's noise settings (`minecraft:overworld`,
  `amplified`, `large_biomes`, `caves`, `floating_islands`, `nether` or `end`) and a `biome_source`, one of
  vanilla's:
  - `minecraft:multi_noise`, with an inline `biomes` list of `{"biome": ..., "parameters": {...}}` or a `preset`
    (`minecraft:overworld` or `minecraft:nether`). Each parameter (`temperature`, `humidity`, `continentalness`,
    `erosion`, `depth`, `weirdness`) is a number from -2 to 2 or a range, `[min, max]` or `{"min": ..., "max": ...}`,
    and `offset` is from 0 to 1. Biomes are picked from the climate like vanilla picks them;
  - `minecraft:fixed`, with one `biome`;
  - `minecraft:checkerboard`, with `biomes` (a biome, a list of them or a biome tag, not empty) and a `scale` from 0
    to 62 (2 when missing or invalid, like vanilla);
  - `minecraft:the_end`, the end's islands and biomes.
- Biomes are vanilla's or plugins'. Dimension types, noise settings, biomes and presets are named: defining them
  inline isn't supported yet.
- Not supported yet, and rejected with a message saying so: `minecraft:flat` and `minecraft:debug` generators,
  plugins' noise settings, and noise settings that generate outside the dimension type's build range (vanilla
  clamps them; its own pairings all fit).

The dimension generates like a vanilla dimension with those settings: terrain, surface, aquifers and ore veins come
from the noise settings, caves and canyons are carved like in the vanilla dimension using the same noise settings,
features are the dimension's biomes' (see [World generation features](#world-generation-features)), and structures
generate where their biomes allow, as in vanilla. Players are placed on the ground near the climate's spawn, like the
overworld's spawn.

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
- A dimension's level stays until the server stops, even when its plugin unloads, since it only uses vanilla's
  generator and entries that stay registered. Its features come and go with their plugins like any features.
- A reload changing a level stem keeps the dimension as it was, with a warning: the change applies after a restart,
  since changing a live level's generator would mix two generators' chunks.
- After a restart without the plugin, its dimension isn't loaded, and its folder is left alone. Players who were in it
  join at the world's spawn.

Players get there with `/dimension <id>`, like `/dimension ruby:crystal_world`, and plugins with
`IPlayer.TeleportToDimensionAsync("ruby:crystal_world")`, which sends a player to the dimension's spawn. Vanilla
clients see a plugin's dimension as a level of that name with its dimension type, sent in login and respawn with the
build height of the type, and its plugin biomes in chunks by their registry ids. There are no portals to plugins'
dimensions, and only the default world has them.
