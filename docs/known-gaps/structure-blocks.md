# Structure blocks

The structure editor, saved settings, SAVE/LOAD/CORNER/DATA, corner detection and rising-edge redstone triggers
are implemented against the cached 26.3 server. `structure_blocks.json` has 29 cases and 82 checkpoints, replayed
by `VanillaStructureBlocks.ReplayStructureBlockReference`. Jigsaw editors and generation are a separate task.

## Exact captured differences

`entities_included_and_ignored` pins four fields. The generic entity serializer writes four fields differently
from vanilla for its armor stand: `attributes` includes Obsidian's default attribute set instead of an empty list;
`PersistenceRequired:0b`, `TicksFrozen:0` and an empty `obsidian:attachments` compound are present where vanilla
omits them. `entitySaveGaps` records the exact vanilla and Obsidian value of each field. The replay checks both
sides before applying these four substitutions to that case's expected template entity NBT. Every other saved
field, the entity list, relative positions, rotations, blocks, block entities, files and messages are compared
from the first action. These are generic entity persistence differences, not a structure-specific serializer.

`strict_false` action 2 separately pins the unsupported wire's dropped redstone count: vanilla drops one item, while
Obsidian drops none when the wire's placement shape turns into air. The new guard prevents a crash when that air
has no `power` property; fixing the generic wire/shape loot path is outside this structure block change. Both
vanilla's count and Obsidian's absence are checked exactly. Chest contents spilling during template replacement
are not pinned: each checkpoint compares total item counts, and the removal flags suppress the spill.

## Asynchronous work

Template file IO runs on workers. Edits publish immutable settings under the tick lock, and asynchronous loads
commit only while the original block entity and editor revision remain current. Changing power alone does not
cancel a pending load. Writes reserve a worker chain in snapshot order; they never await under the tick lock,
TransferLock or a chunk lock. The Obsidian-only gated test holds a write pending while another thread acquires
all three locks and changes the source; the file retains the earlier snapshot.

A cached redstone LOAD with its destination loaded runs immediately inside the neighbor reaction. A file/cache
miss or missing destination chunks is queued to the entity action path, so its placement can occur later than
vanilla's synchronous neighbor reaction. A SAVE whose area needs generation similarly snapshots after that area
is ready. The fixture compares completed actions by draining that queue, without advancing unrelated game ticks.
The existing chunk-ticket generation waiter keeps the area resident until snapshotting/placement finishes.

## Review regressions covered

Template names share a portable-file check before SAVE, LOAD, DETECT, redstone actions, `/place template` and
resource fallback. Traversal and empty/dot directory segments cannot reach file or resource lookups. Windows
reserved names (`con`, `nul`, `com1`, `lpt9` and their extensions) and directory names ending in a dot are rejected
on every platform, matching 26.3's `FileUtil.isPathPartPortable`. `review_portable_paths` captures these rules.
The converter appends `.nbt` before vanilla validates its segments: `test:` and `test:folder/` are valid filenames
`.nbt` and `folder/.nbt`, so they remain accepted. Editor identifiers are retained for saved data and feedback;
the shared portable-file check gates their use. Obsidian-only tests plant a real escaped resource outside the
resource root and require both the block's LOAD and `/place template` to reject it.

Eviction removes the current cache entry identity. A pending read can publish only against that same identity;
a worker completing after CORNER eviction retries in the current generation. An explicitly gated test covers
both a pending template and a pending missing-file result, with no sleeps or timeouts.

After every non-strict template placement's shape pass, placed positions notify their neighbors. The two
`review_load_notifications_*` captures distinguish a neighboring lamp lighting normally from remaining unlit
under strict placement. `review_place_command_strict` captures the command's trailing `strict` argument and the
same distinction, including vanilla's success feedback. `/place template` checks the unrotated position through
position+size chunk range, including the far endpoint, as 26.3 does; it returns "That position is not loaded"
without loading or generating missing destination chunks. `review_place_command_unloaded` records that message,
and an Obsidian-only test covers the inclusive endpoint chunk and verifies no chunks were generated.

## Limits of the evidence

The capture covers one armor stand and a chest with items; it does not establish every entity type's saved NBT,
passenger tree, hanging-entity attachment transforms, every block entity's side effects, or palette variants from
external multi-palette templates. SAVE creates a single palette. The current generic entity persistence and
`StructureTemplate` entity placement determine those broader behaviors. Like other placed game-master items,
item-carried `block_entity_data` is not imported by the placement path. Typed settings from chunk/template NBT
are loaded and sent to clients; the placement hook records the placing player's name as author.

`/place template` shares the generated-file cache and placement engine. The 26.3 path is
`<world>/generated/<namespace>/structure/<path>.nbt`, including namespaced subdirectories. Author lives in the
block entity and in the in-memory template; vanilla does not include it in the template file, so a file reload
reports the default `?` author. Redstone SAVE updates only the cache, and a powered CORNER evicts it.
