# Plugin blocks, entities, tags and data

Plugins add blocks in code or from JSON files that Obsidian's source generator turns into code, add entity types in
code, and add tags, biomes, dimension types and damage types with data pack files, like NeoForge and Fabric mods.
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
entries are added or their data changes. Plugin biomes don't generate anywhere yet, and the Obsidian client colours
biomes from compiled tables, so plugin biomes have no tint there yet.

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
- A biome is read whole, like vanilla reads it. Its `carvers` and `features` refer to vanilla's configured carvers and
  placed features by identifier (plugins can't define them inline yet). Where vanilla would log and drop a carver,
  feature or spawner it can't read, the file is left out instead.
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
