# Builtins

Builtins are optional features compiled into Obsidian itself. They use the plugin API's registration and lifecycle
contracts and appear in the manager's loaded feature list, so commands, events, services and cleanup share the
existing lifecycle. Enabled builtins are instantiated directly in the server assembly before external plugins load.

Enable them in `config/server.json` and restart the server:

```json
{
  "builtins": {
    "worldedit": true
  }
}
```

Every builtin defaults to disabled. Changing this setting requires a restart. The `worldedit` id is reserved
while the builtin is enabled; do not also install an external plugin with that id.

## WorldEdit

WorldEdit lives in `Obsidian/Builtins/WorldEdit` and uses the plugin API. All commands
require an operator and a player context. Use `//help` (or `/worldedit`) for a command summary.

| Commands | Behavior |
| --- | --- |
| `//wand` | Give a selection wand in an empty inventory slot. Left-click selects position 1; right-click selects position 2. |
| `//pos1 [x y z]`, `//pos2 [x y z]` | Select your current block position or explicit coordinates, including integer `~` offsets. Comma-separated coordinates also work. |
| `//sel`, `//deselect` | Clear the selection. |
| `//size` | Show inclusive cuboid dimensions and block count. |
| `//set <pattern>` | Fill the selection. |
| `//replace <from-blocks\|*> <pattern>` | Replace matching block types or explicit states. Comma-separated source blocks are supported. |
| `//walls <pattern>` | Fill the four vertical sides. |
| `//outline <pattern>`, `//faces <pattern>` | Fill all six cuboid faces. |
| `//copy`, `//cut` | Copy the cuboid relative to your feet; cut also clears its blocks. |
| `//paste [-a] [-s]` | Paste relative to your current position; `-a` skips air, `-s` selects the pasted cuboid. |
| `//rotate <degrees>` | Rotate the clipboard around its origin on the Y axis, in multiples of 90 degrees. |
| `//flip [x\|y\|z\|direction]` | Mirror clipboard positions and directional block states around the copy origin. Defaults to the direction you face. |
| `//undo`, `//redo` | Replay your last edit in its original dimension. |
| `//expand <amount> [direction]` | Expand the selected cuboid in one direction. |
| `//contract <amount> [direction]` | Move the opposite face inward toward the direction. |
| `//shift <amount> [direction]` | Move the selection without changing blocks. |
| `//stack [count] [direction]` | Place adjacent copies of the selection. Defaults to one copy. |
| `//move [distance] [direction]` | Move blocks and the selection, including overlapping moves. Defaults to one block. |
| `//sphere <pattern> <radius>`, `//hsphere <pattern> <radius>` | Solid or hollow sphere centered on your block position. |
| `//cyl <pattern> <radius> [height]`, `//hcyl <pattern> <radius> [height]` | Solid or hollow cylinder starting at your feet and growing upward; default height 1. |

Directions are `north`, `south`, `east`, `west`, `up`, `down` (or their first letters), and `me`/`forward`.
Direction defaults to where you are looking, including up/down. Clipboard flips also accept the axis names.

Patterns accept block identifiers, block states and weighted mixtures, for example `stone`,
`oak_stairs[facing=east,half=top]` and `80%stone,20%mossy_cobblestone`. Unweighted mixtures choose each block equally.
Block NBT literals, entities, biomes, schematic files and advanced brushes are not supported in this initial builtin.
Clipboard transforms cover positions and common directional block properties; custom orientation stored inside block
entity NBT is copied unchanged.

### Tools, bounds and history

The selection wand is a vanilla wooden axe with a display name and a `minecraft:custom_data` component containing
`obsidian:worldedit_tool: "wand"`. This metadata survives normal item serialization and needs no client mod.
Only marked tools used by operators are intercepted while the builtin is enabled. Ordinary wooden axes retain their
normal behavior. Issuing a wand does not replace or merge into an existing item.

Selections and edit/clipboard sizes are limited to **32,768 blocks**. For shapes, the bounding cuboid also obeys that
limit; for moves, the union of source and destination does. All affected positions must be inside build height and
complete loaded chunks. WorldEdit does not generate or load missing chunks. Operations are serialized, do not run
neighbor physics, and do not drop replaced blocks or container contents.

Clipboard snapshots and history preserve saved block entity data, including inventories. Plugin block entity data
is captured as of the last change published by the level, matching chunk-save semantics. Ten edits are retained
per player. New edits clear redo history. An edit interrupted after writes retains the changes for undo; unloaded
chunks must be loaded again before replaying them. Undo/redo are block edits, not an atomic transaction against
other plugins, players or ongoing world ticks.

Clipboard, selection and history are memory-only and clear on disconnect or builtin shutdown. Selection points cannot
span dimensions. A clipboard can be pasted in another dimension; history targets the dimension where the edit ran.

## Adding another builtin

Add a feature under `Obsidian/Builtins/<Name>` deriving from `Builtin`, with its identity and lifecycle callbacks.
Add direct construction to `Builtin.Enabled`, gated by `Builtins:<id>`, and document its disabled-by-default toggle
in the sample configuration/schema. There is no separate project, assembly packaging or assembly load context.

`MapCommands` and `MapEvents` discover only the builtin's namespace and its child namespaces. Normal server
discovery excludes builtin namespaces, so disabled features remain inactive. Explicit registration through the
plugin registry also remains available. A small resource adapter supplies the existing container's metadata and
file-access contract; it carries no compiled code or archive payload.
