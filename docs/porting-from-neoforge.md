# Porting from NeoForge

Obsidian's mod system is close to NeoForge's (see [mod-system.md](mod-system.md)), and a mod's data in vanilla's
formats works on Obsidian as it is. Obsidian.Porting converts what NeoForge reads differently, so a mod's data and
metadata move over in one step:

```text
dotnet run --project Obsidian.Porting -- <mod folder or jar> <output folder>
```

The input is the mod's jar, or a folder laid out like it (say, `src/main/resources`). The output folder, which must
be new or empty, gets the files an Obsidian plugin ships besides its assembly (see
[plugin-archives.md](plugin-archives.md)), and `porting-report.txt`, which the tool also prints. The tool exits
with an error when it could write nothing:

| Mod's file | Output |
| --- | --- |
| `META-INF/neoforge.mods.toml` (or `mods.toml`) | `obsidian.mod.json` |
| `data/<ns>/neoforge/biome_modifier/*.json` | `data/<ns>/biome_modifier/*.json`, Obsidian's biome modifiers |
| Other `data/` and `assets/` files | Copied unchanged |
| Data only NeoForge reads, Java code, everything else | Left out and reported |

## Metadata

`obsidian.mod.json` follows `neoforge.mods.toml`'s field names, so the first `[[mods]]` entry maps directly:
`modId`, `version`, `displayName`, `authors` (split at commas), `description` and `displayURL` (as `projectUrl`).
A version of `${file.jarVersion}` takes the jar's `Implementation-Version`; another placeholder is reported and
written as `1.0.0`. Its `[[dependencies.<modId>]]` keep their `modId`, `type` (or the older `mandatory`),
`versionRange`, `ordering` and `side`, except those on `neoforge` and `minecraft`, which Obsidian is. `assembly`,
which NeoForge doesn't have, is the mod id in Pascal case: make it the C# plugin's assembly. Other fields, like
`license`, `logoFile` or `[[mixins]]`, are reported.

## Biome modifiers

Every NeoForge biome modifier type converts to an Obsidian modifier (see
[plugin-content.md](plugin-content.md#biome-modifiers)):

| NeoForge | Obsidian |
| --- | --- |
| `neoforge:add_features` (`features`, `step`) | `add.features` of the step |
| `neoforge:remove_features` (`features`, optional `steps`) | `remove.features`, of the steps or of every step |
| `neoforge:add_spawns` (`spawners`) | `add.spawners` |
| `neoforge:remove_spawns` (`entity_types`) | `remove.spawners` |
| `neoforge:add_carvers`, `remove_carvers` (`carvers`) | `add.carvers`, `remove.carvers`; a carving `step` is ignored, as 1.21.11 has none |
| `neoforge:add_spawn_costs` (`entity_types`, `spawn_cost`) | `add.spawn_costs`, by entity type |
| `neoforge:remove_spawn_costs` (`entity_types`) | `remove.spawn_costs` |
| `neoforge:none` | Left out: it does nothing |

`biomes` becomes Obsidian's `biomes` and `exclude`. Identifiers, tags and lists stay as they are; `neoforge:or`
becomes a list, each entry once, `neoforge:and` of one set and `neoforge:not`s becomes that set less the others, and
`neoforge:not` inside an `and` becomes `exclude`. An `or` of `not`s is every biome less what they all exclude and
the `or` doesn't list. The tool doesn't know what tags hold, so a set is only converted when that doesn't matter:
`or[not #a, not #b]` (every biome less what both tags hold) or `or[not #a, minecraft:plains]` can't be listed
without it. What Obsidian's sets can't say is reported and left out: every biome (`neoforge:any`, or a `not` on its
own), an intersection of two listed sets, sets like those, and other holder set types. Features, carvers and entity
types must be listed sets, and added features and carvers can't join a tag with other entries, which may share
entries NeoForge adds once and Obsidian twice; spawn costs can't be given to an entity type tag, since Obsidian keys
them by type.

NeoForge 1.21.11's `add_spawn_costs` and `remove_spawn_costs` change the costs in every biome, whatever their
`biomes` say. Obsidian's modifiers have no "every biome", so the converted costs keep to the modifier's biomes and
the report notes each one: list every biome the costs should change in to match NeoForge.

NeoForge applies every modifier's additions, in identifier order, before any removal, like Obsidian does, so
consecutive modifiers (in identifier order, in one namespace) with the same biomes merge into one file, named after
the first: what they generate stays the same, features keeping their order in each step. Modifiers with
`neoforge:conditions` are left out and reported, since Obsidian doesn't read load conditions.

## What's left out

- The mod's Java code. Obsidian's API is C#, and its systems are Obsidian's own rather than NeoForge's classes, so
  blocks, items, entities, menus, events, commands and network payloads are written again as an Obsidian plugin (see
  [plugin-content.md](plugin-content.md)). The report counts the `.class` files.
- NeoForge's own data: data maps (`data_maps/`), global loot modifiers (`loot_modifiers/`), structure modifiers
  (`neoforge/structure_modifier/`) and anything else under `data/<ns>/neoforge/` or `data/neoforge/`.
- Data files with `neoforge:conditions`.
- Everything outside `data/` and `assets/`: `pack.mcmeta`, mixin configs, access transformers, logos.

Tags need nothing: Obsidian reads NeoForge's `remove` lists and the shared `c:` tags. Copied files Obsidian doesn't
read yet, like recipes or loot tables, are harmless.
