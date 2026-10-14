# Plugin archives

A plugin (or mod: a plugin that adds content) ships as one `.obby` file in the server's `plugins/` folder. Obsidian
reads two formats. Version 2 is the current one; version 1 files, packed by older Obsidian.MSBuild releases, still
load.

## Version 2

A version 2 archive is a ZIP file, named `.obby`. Paths use `/` and are case-sensitive; none may start with `/` or
contain `..`.

| Path | Contents |
| --- | --- |
| `obsidian.mod.json` | Required. The plugin's metadata, see [below](#metadata). |
| `<Assembly>.dll` | Required. The plugin's main assembly, named by `assembly` in the metadata. |
| `<Assembly>.pdb`, `<Assembly>.deps.json` | Optional. Symbols, and the dependency manifest that lists the libraries to load. |
| `*.dll`, `*.pdb` | Libraries the plugin uses, as `dotnet publish` puts them beside the main assembly. |
| `assets/<namespace>/...` | Optional. Client resources (textures, models, translations), laid out like a resource pack, see [below](#assets). |
| `data/<namespace>/...` | Optional. Server data laid out like a data pack; block, item and entity tags are read. |

Entries may be stored or deflated. Obsidian reads the file into memory once and decompresses each file when it is
first needed, so the file on disk can be replaced while the plugin runs, which reloads it.

## Metadata

`obsidian.mod.json` follows the fields of NeoForge's `neoforge.mods.toml` where the two overlap, written as JSON
(comments and trailing commas are allowed):

```json
{
  "modId": "ruby",
  "displayName": "Ruby",
  "version": "1.2.0",
  "assembly": "Ruby",
  "apiVersion": "1.0.0",
  "authors": ["Tides"],
  "description": "Adds rubies.",
  "projectUrl": "https://example.com/ruby",
  "side": "both",
  "dependencies": [
    { "modId": "gemlib", "type": "required", "versionRange": "[1.0,2.0)", "ordering": "after", "side": "both" }
  ]
}
```

| Field | Default | Meaning |
| --- | --- | --- |
| `modId` | | Required. The unique id other plugins depend on, and the namespace of what the plugin registers. |
| `version` | | Required. A version like `1.2.0`; a prerelease label (`1.2.0-beta`) is accepted and ignored for ordering. |
| `assembly` | | Required. The main assembly, without `.dll`. |
| `displayName` | `modId` | The name shown to players and in logs. |
| `apiVersion` | empty | The Obsidian.API version the plugin was built against. |
| `side` | `both` | Where the plugin runs: `both`, `server` or `client`. A server skips `client` plugins, and the Obsidian client, which loads mods from its `mods` folder, skips `server` ones. |
| `dependencies` | none | Other plugins, see below. |

Each dependency has:

| Field | Default | Meaning |
| --- | --- | --- |
| `modId` | | Required. The other plugin's id. |
| `type` | `required` | `required`: must be present with an accepted version. `optional`: if present, must have an accepted version. `incompatible`: this plugin doesn't load while an accepted version is present. `discouraged`: only warns. |
| `versionRange` | any | Accepted versions, as a Maven/NuGet range (`[1.0,2.0)`) or a minimum version (`1.0`). |
| `ordering` | `none` | `after` loads the dependency first, `before` loads this plugin first. With `none`, a present dependency still loads first (so that the plugin can use its types), unless other orderings contradict that for an optional one. A required dependency can't be ordered `before`: Obsidian has to load it first. |
| `side` | `both` | The side the dependency applies to; on the other side it is ignored. |

Plugins load in dependency order; among plugins that are free to load, by id. The constraints of plugins already
loaded apply to plugins that arrive later too: a plugin another one declares incompatible doesn't load. A plugin that
can't load yet (say, its required dependency is missing) waits, and is tried again whenever plugins load or unload.

The services a plugin registers can take, in their constructors, its other services, the singletons registered by
the plugins it depends on, `IServer`, the server configuration and loggers. Its `[Inject]` properties can also get the
server's other services.

## Assets

The Obsidian client reads a mod's `assets/` as a resource pack, one per mod in load order, above vanilla's assets and
below the player's resource packs (and servers' packs). It's laid out like a resource pack's, usually under the mod's
own namespace, its `modId`:

| Path | Contents |
| --- | --- |
| `assets/<ns>/blockstates/<block>.json` | The models of the states of the block `<ns>:<block>`. |
| `assets/<ns>/models/...` | Block and item models, referred to as `<ns>:block/<name>` and `<ns>:item/<name>`. |
| `assets/<ns>/textures/block/...`, `textures/item/...` | Textures, referred to as `<ns>:block/<name>` and `<ns>:item/<name>`, each with an optional `.png.mcmeta` that animates it. |
| `assets/<ns>/items/<item>.json` | The definition (the model) of the item `<ns>:<item>`. |
| `assets/<ns>/lang/<language>.json` | Translations, merged with vanilla's and other mods' per language. |
| `assets/<ns>/models/entity/<path>.json`, `textures/entity/...` | Entity model layers and textures, for the mod's entity renderers (see [plugin-content.md](plugin-content.md#client)). |
| `assets/<ns>/particles/<particle>.json` | Particle sprite sets, for the particle `<ns>:<particle>`. |
| `assets/<ns>/sounds.json`, `sounds/...` | Sound events and their files. |

For a block `ruby:ruby_block` and its item:

```text
assets/ruby/blockstates/ruby_block.json
    {"variants": {"": {"model": "ruby:block/ruby_block"}}}
assets/ruby/models/block/ruby_block.json
    {"parent": "minecraft:block/cube_all", "textures": {"all": "ruby:block/ruby_block"}}
assets/ruby/textures/block/ruby_block.png
assets/ruby/items/ruby_block.json
    {"model": {"type": "minecraft:model", "model": "ruby:block/ruby_block"}}
assets/ruby/lang/en_us.json
    {"block.ruby.ruby_block": "Block of Ruby"}
```

A `pack.mcmeta` is optional; a mod that has one gets its filter and overlays applied like a resource pack's. Only
`assets/` (and overlays' `assets/`) are read, within the client's resource pack limits. A mod's block draws each face
in the render layer its texture needs: solid when it's opaque, cutout with fully transparent pixels, and translucent
with partly transparent ones (an animated texture's most transparent frame decides). A block or item texture's
`<name>.png.mcmeta` animates it like vanilla's water and fire: its `animation` gives the frame size (`width` and
`height`, square by default), the `frames` in order (indices into the texture's frames, left to right then top to
bottom, or `{"index": 2, "time": 5}`; all of them in turn by default), the ticks each one shows (`frametime`, 1 by
default) and whether one blends into the next (`interpolate`). Version 1 archives have no assets.

## Reloading

Changing a plugin's file reloads the plugin; deleting it unloads it. The plugins that depend on it reload, or wait,
with it, since they use its types. Unloading removes everything the plugin registered (commands, event handlers,
services, payload channels, argument parsers), and what it added through the server's services (dialogs, recipes,
scoreboards, boss bars, generators; see [mod-system.md](mod-system.md#what-host-services-add)), so its assemblies,
which load into a collectible load context, can be freed. Unloading stops new calls into the plugin (its event and payload handlers, commands and command suggestions),
and the plugin is released (its unloading callback runs, and its services are disposed) once the running ones end,
after the plugins that depend on it. A plugin may reload itself from one of its own commands.

Content a plugin registered, like its items, keeps its identity and ids while the server runs: reloading the plugin
gives the same items their new behaviour, and unloading it leaves them registered, without behaviour, until the server
stops.

A plugin must not leave references to its own types with the server or other plugins outside what it registered, or it
can't unload, and it can't load or unload plugins from its own load, ready or unloading callbacks.

## Version 1

Version 1 archives begin with `OBBY`, then the API version, a SHA-384 hash of everything after the header (checked, to
catch damaged files), an optional signature of that hash (ignored: plugins aren't signed), and the plugin's metadata
(assembly, version, name, id, authors, description, project URL and dependencies with an id, version range and
required flag), followed by a table of flat file names and their stored (deflated or plain) contents. If one of the
files is `obsidian.mod.json`, its metadata replaces the header's, which gives version 1 plugins the dependency types,
ordering and side above.
