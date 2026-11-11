# Inventory and world lock order

Audited from 18321fecd, after placement #715, block entity ticking #711, pending teleports #712 and furnace recipe
awards #716. The original graph and placement reproduction were committed before #715's implementation. This
follow-up removes the snapshot wait edges and adds one bounded placement retry.

## Rule and graph

**Wait for inventories only outside world tick, entity transfer and chunk entity/generation locks. Under those
locks, try every required inventory; on contention release everything and defer or retry without those locks.**
Release inventory after a retry's availability wait, before re-entering tick. Several inventories acquired by waiting
belong to one ranked set; separate nested acquisitions do not inherit that ordering. Ordinary inventory locks are
non-reentrant. Final host snapshot serialization reuses the inventories its explicit admission already owns;
preparation can read already captured inventory NBT without claiming ownership of another inventory.
The rule governs host acquisition; virtual plugin inventory getters retain their previous tick/generator and
own-inventory scope, and run before transfer, chunk entity locks and ambient host admission. The same applies to
component Value/copy delegates reached by item NBT conversion and equipment network codecs; admission only sees
fully prepared host NBT and equipment bytes.

An arrow means acquisition while holding the left side. `wait` includes synchronous and asynchronous waiting;
`try` means `Wait(0)`, abandoning partial acquisitions on failure.

| Path / lock | Edges and acquisition behaviour |
| --- | --- |
| Level simulation gate | Serializes ticks, saves and unloads (async wait), enclosing player gates and tick sections. Code holding tick, transfer or a player gate must not wait for simulation gates (`MustNotWaitForGates`). |
| Player tick gate | `tickGate -> player inventory` (wait) for item ticks, grants and saves, with own-flow reuse. Queued block uses only try the player gate. Cross-player gate waits are prohibited. Incoming placement does not acquire it. |
| Inventories / windows | Each `BaseContainer.TransactionLock` is a semaphore. `Lock` waits synchronously, `LockAsync` asynchronously, `TryLock` does not wait. `LockWindowAsync` and composites acquire distinct inventory parts in stable `LockRank` order and release in reverse; there is no additional window mutex. |
| Item hooks / menu callbacks | Inventory-tick hooks keep `inventory -> plugin InventoryTickAsync -> tick` (wait) for an awaited world write. Their atomic inventory view is unchanged. Deferred damage/death work follows unlock. Menu callbacks can also own inventory while writing the world. |
| Placement | `tick -> hand` (try), revalidating and committing the block and consumption together. Contention gets at most one availability wait outside tick, then another `tick -> hand` try. An inherited tick/transfer lock disables the wait. |
| Block use (#701) | Hand snapshot waits with inventory alone, releases it, then reads under tick. `BlockUse.TryCommit` takes `tick -> hand -> destination` with try-locks for both inventories. Effects publish after both inventories unwind, still under tick. BlockUseReturn.Give/Exchange apply returned items under the same hand lock; overflow drops with the published effects. |
| Dispenser (#729) | Snapshots the selected stack under inventory alone and releases it before preparation or a tick wait. The commit takes `tick -> dispenser inventory` by **try**, revalidates resident identity, revision and stack, and changes slot/world state synchronously. It uses block-use effects to defer notifications until inventory unlock. Pending loot and box component/factory preparation run outside tick/transfer/click/chunk locks. The save rendezvous in `DispenserAtomicity` regresses the old `inventory -> tick` inversion independently of #719. |
| Furnace / campfire | Tick operations try their machine inventory and skip busy ticks. Campfire insertion tries hand then campfire and refuses before changing either if busy. Campfire update data tries once, retains its last complete client projection and queues another update if busy. |
| Container item access | Under tick, `ContainerItemAccess.Enter` tries inventory; otherwise it waits. Vehicle access always tries. Protected `LockInventory` refuses under tick and forbids awaits/other locks in its scope. Loot preparation finishes outside tick/transfer/click locks, or is queued. |
| Entity transfer | Pickup owns hand before waiting for transfer. Transfer precedes chunk `EntityLock` and grid locks. Never hold two levels' transfer locks, await, load chunks or invoke plugin code under transfer. |
| Surface lifting / catalysts | Surface rises take tick -> transfer, revalidating level and position before the host lift. Virtual geometry is prepared before transfer. Catalyst advancement conditions match during the death; grants queue to background work instead of waiting for the player's gate from a level tick. |
| Region snapshots | Generator -> tick -> each inventory (try, separately) captures virtual slots and converts their complete item NBT without transfer or ambient admission. Then transfer -> the captured inventory set (try), validating host content revisions and membership before retirement. Host-only hashing, retirement and serialization share the captured members, prepared NBT and equipment bytes; entity serialization also takes chunk `EntityLock`. Contention/validation failure takes no dirty marks and retires/removes nothing. Whole-region unload retains rollback ownership until all chunk snapshots succeed. |
| Plugin preservation | Final plugin Save runs without tick or new inventory locks. Host preservation takes tick -> inventory admission (try), revalidates identity, freezes the inventory and saves its current host-owned contents. Fresh plugin fields are cached before admission can defer; virtual slot capture runs with tick and only its own inventory. A call inheriting tick, or a busy admission, queues preservation with an unloading hold released on execution or level disposal. |
| Chunks / lighting / structures | Live writes take tick -> `Chunk.BlockLock`; runtime light uses tick -> propagation reader/writer lock. Chunk I/O and region lookup finish before write sections. Structure-state serializers may call plugins: they finish in their existing tick scope before snapshot admission adds transfer/inventory locks. |
| Notification queue | Tick -> `BlockNotificationPipeline.sync` is a short blocking monitor. Drain releases `sync` before world reads, hooks and awaits; it adds no reverse tick/inventory edge. Placement's existing synchronous callbacks retain their existing lock scopes. |
| Other bookkeeping | Viewer-list, player-grid membership and `WindowSnapshots.gate` are short monitors; callbacks/packet handlers run outside them. Loot uses `LootGenerationLock -> inventory -> LootLock`; saves/components use inventory -> LootLock. Data entities and attachments have their data monitors. Plugin call leases are nonblocking lifetime accounting. #716's `recipeEventsSync` is taken under furnace/window inventory or the player's tick gate, and released before awaits, criteria delivery or other lock acquisition. |


## Snapshot admission and busy data

The inherited vehicle cycle needed no plugin: a ranked window acquired vehicle inventory and waited for the hand;
pickup owned the hand and waited for transfer; a region snapshot owned transfer and chunk EntityLock and waited
for vehicle inventory. The bounded rendezvous test failed on the original code with
`save=False, window=False, pickup=False`; cleanup released the test-held hand and joined all pending operations.
After the fix the snapshot abandons inventory admission, freeing transfer for pickup and the window to finish.

`InventorySnapshot` first reads each container's virtual slots separately under a tried inventory lock, and fully
encodes their item NBT there, including nested items and component Value/copy delegates. It canonicalizes that NBT
into private host-owned tags before transfer, chunk entity locks or thread-static admission. Conversion failures
therefore precede dirty marks and retirement. Preparation mode covers the initial slot reads too: a component
copy or virtual getter cannot make an auxiliary base save wait while tick/current inventory are held. It then
visits the captured block/entity serializers to encode stacks outside their Items lists too (e.g. lectern books,
drops and projectiles). Mob equipment keeps its existing `ObsidianEquipment` wire format: its full item bytes are
prepared here as well, before `NetworkBuffer.WriteItemStack` can reach a plugin network writer under transfer.
Both item NBT and equipment-byte caches validate the source stack's nonvirtual version; a replacement or mutation
requires recapture. In this mode,
known inventories return prepared NBT; an auxiliary base save may only try its own inventory and defers the attempt
if busy. This thread-static preparation mode owns no inventory set and does not enforce admission on plugin code.
After leaving it, final admission validates inventory/item versions and rechecks the serializers for replaced stack
references before retirement. A new or changed source retries before any dirty marks. Final item ToNbt calls only
copy the privately prepared host NBT, so component delegates cannot run through a different serializer under transfer.
Machine counters and other live host fields are still saved under the final inventory/loot locks.
Inside admission, host serializers copy prepared NBT and hash host-owned data; they never convert captured stacks
or reach a component delegate. The same prepared item path serves block containers, vehicles and preservation.
No plugin Save override, virtual inventory getter, component Value/copy delegate, structure serializer or await runs
in the admitted scope. This is save work only, not a new lock per block entity per tick.

Admission validates host content revisions and the nonvirtual versions of every returned stack, never
lock-acquisition counts: a read-only base container save of an earlier member does not invalidate it. Engine indexer writes also record a content change when a custom SetItem
override stores slots outside the base array. The protected inventory scope already compares public revisions;
it publishes an actual change to the host stamp before releasing its existing lock, including direct custom SetItem
writes. Its read-only use adds no stamp. Final validation never dispatches to a plugin's virtual Revision getter.

Block and vehicle membership is captured once before virtual reads and validated again after them, before dirty
marks or retirement. Public plugin dictionary and position setters do not obey transfer locking. If preparation
published/moved a member, the attempt leaves the chunk untouched and retries (or an eager save defers). Dirty
hashing, block retirement, block serialization and entity serialization all consume those captured members;
serialization cannot discover an inventory that admission omitted. This does not synchronize arbitrary public
setters after validation: their writes have no atomic relation to final unload. Ordinary saves detect later block
membership changes on the next save; broader synchronization of public setters is outside this repair.

Structure state is always prepared before admission for an attempted snapshot, even when the preliminary chunk
marks look clean: independently synchronized attachments can make the final write decision true. A preparatory
serializer call does not itself cause a chunk write. Only the final dirty-state decision reserves a write.
Structure-serializer failures are captured before admission and rethrown at the original post-retirement write
point, preserving unload-failure cleanup without adding inventory/transfer locks around plugin code.

- An eager save skips the **whole chunk**, retaining its unsaved state and entities for a later save.
- An explicit save or unload releases generator, tick, transfer and inventory locks, awaits the busy inventory's
  availability, releases it, then retries admission against current chunk and entity membership. It reports success
  only after admitting and serializing the same captured members and validated item NBT. A content revision or
  membership change retries outside the locks, yielding between attempts. Explicit saves/unloads make at most
  **eight attempts**, each availability wait capped at **250 ms** (at most two seconds of inventory waiting).
  Exhaustion throws `TimeoutException` before taking marks, retiring inventories or reserving writes: the chunk
  remains resident, the prior disk image stays intact, and a later save/unload can retry. This bounds retry work,
  not arbitrary plugin callbacks, generator gates or disk I/O. Eager saves make only one non-waiting attempt.
  A caller already owning tick/transfer cannot unwind its outer lock: a busy explicit snapshot throws without
  changing data, leaving it for a legal save; an eager attempt still skips it.
- A chunk unload retires all its containers only after its entire set is admitted. A busy later container in that
  chunk cannot leave earlier containers retired. The final snapshot freezes contents together with entity removal.
- Whole-region unload first seals publication and structure notifications, without retiring resident chunks.
  Its per-chunk generator locks can be plugin-defined; retaining earlier generator/inventory locks across another
  chunk's callbacks/await would add prohibited lock scopes. Instead, snapshots stage privately until all chunks
  succeed. No pending write, slot removal or unload callback is published before that point. If a later chunk times
  out or fails preparation, rollback restores earlier chunks' inventory and block-entity retirement flags,
  entity membership/grid and `EntitiesUnloaded`, owned chunk/entity attachment retirements, lighting and prior
  fluid/block tick tracking, and dirty marks; reserved writes and private buffers are discarded. Generation
  completion callbacks were never detached. The publication seal is then lifted. The original disk images remain
  intact and all chunks can be edited and saved/unloaded again. The region itself retires only after every snapshot
  succeeds. This is a save/unload-only transaction, with no inventory lock held across an await or new plugin scope.
- Plugin preservation leaves a busy entity resident and its inventory unretired. Deferred work keeps its plugin
  alive, tries again on a later entity-action drain, revalidates identity, then closes retired windows. A successful
  final plugin Save is cached immediately, before a busy admission can defer it, so consuming the change signal
  cannot lose plugin fields. It runs outside tick; validated, prepared item NBT overlays its contents under admission.
  Placement's late-unloading preservation defers the entire operation. Queued leases are owned by the level:
  disposal releases undrained work after its final save, including failure paths and enqueue/disposal races,
  and drops the queued plugin delegate so it cannot retain an unloaded context.
- A busy campfire client projection keeps the previous complete projection and requests another update. Its disk
  snapshot follows normal admission and always contains its current inventory.

The regression tests read committed chunk/entity payloads before server disposal can perform another save, then
load those exact NBT bytes through a separate region. They verify final item identities/counts and absence of extra
stacks for the vehicle cycle, vanilla and plugin-style container snapshots, unload retirement, preservation, and
campfire projection. `SnapshotInventoryConsistency` additionally covers deferred plugin fields, disposal leases,
late vehicle/container membership, virtual getters reaching a foreign transfer or auxiliary inventory, changed
capture revisions (including engine indexer writes to plugin storage), and structures dirtied after preliminary marks.
`SnapshotInventoryPreparation` keeps nested read-only callbacks and a real read-only transaction after capture
active until save/unload/eager completion, checks
bounded failure and recovery under persistent mutation, and reloads component-bearing containers/vehicles after
copy delegates enter a foreign transfer or save another inventory, including lectern books and dropped items.
A replaced entity stack is detected before unload retirement and recaptured. It also checks that a throwing copy delegate
cannot partially retire an unload or damage the prior disk save. A busy auxiliary-copy rendezvous proves that
world writes finish while that inventory remains held, before the snapshot's outside-lock wait is released. Tests use explicit signals and bounded cleanup,
not sleeps or statistical scheduling.

`SnapshotInventoryRecovery` reloads mob equipment whose network writer enters a foreign transfer lock, and checks
block writes, lighting, tick tracking and attachment writes after region/single-chunk admission timeouts. Its
multi-chunk rollback test exhausts a later container after an earlier snapshot removed an item entity, then edits
and reloads both inventories and that entity, proving the aborted snapshot neither published nor lost data.
The whole test assembly enables `ComponentExecutionGuard`: plugin-supplied defensive copies, persistent/network
codecs, hash codecs and custom component overrides throw before running under snapshot admission or transfer.
Each test also retains violation notifications across its async work, so catching the exception cannot hide it.
Built-in API copy/codec delegates are host code; nested plugin delegates retain their own guards. The guard is
disabled in production and adds no locks. It does not remove the separate fallback/eviction boundaries below.

## Placement retry

After the first busy hand acquisition, placement waits for at most **50 ms**, one game tick, outside tick/transfer,
using cancellation-aware ranked inventory acquisition. It releases that handle before the second tick-locked
try. This bridges a short inventory hook without leaving a click pending indefinitely. Both attempts validate
resident chunk, target, actor and hand again; only a completed commit consumes one item. A validation refusal is
not retried. An inherited tick/transfer lock refuses immediately because this method cannot unwind its caller.

No statistical refusal rate was measured. The deterministic contention harness clears the hand after the first
failed try and proves that the second try succeeds and consumes once, or refuses after the hand, target, actor or
chunk changes. A hook lasting beyond the wait, or fresh contention on the second try, still refuses the click.
The existing packet path resends clicked/adjacent blocks and hand before acknowledgement, undoing client
prediction; the player can click again. There is no spin or unbounded retry.

## Remaining boundaries

- Failed-travel fallback (`Entity.KeepWhereItLeft -> Region.TryKeepUnplaced`) also serializes an entity under
  transfer/chunk EntityLock. For a container vehicle that still waits for inventory. It needs deferred fallback
  admission retaining the departure's chunk pin and arrival ownership: simply returning false on contention would
  discard an entity that neither level accepted. Item-bearing entities there can also reach component copy
  delegates under transfer: these fallback serializers do not enter regular snapshot preparation. This is separate
  from recurring region snapshots. The generic
  `Region.Evict(keepSaved: true)` serializer has the same boundary; its normal plugin-unload caller only keeps
  plugin-owned entity types (built-in vehicles spawned by a plugin use `keepSaved: false`).
- Factory preparation still calls `BlockEntityComponents.ApplyContainer` on an unpublished inventory under tick
  and hand. Normally uncontended, a plugin factory publishing/reusing an inventory can make it block. Moving
  factory/component preparation requires managing plugin effects and lifetime; skipping transferred contents
  would lose items. Creation/loading and factory update-data callbacks need the same separate design.
- Host content revision tracking observes base mutations, engine indexer writes, and changes reported by the
  protected inventory scope.
  Returned stacks' nonvirtual versions also catch count/component changes to objects outside base storage.
  A plugin that replaces its own backing slots directly while bypassing those mutation paths and the protected
  inventory scope cannot be validated by a
  host-only snapshot through its arbitrary virtual Revision getter. Such custom writers need an explicit host
  mutation notification design; lock acquisition itself is not a safe substitute for a content change.
- Standalone container Save/SnapshotContents APIs still block outside host admission. Plugins must not call those
  APIs while owning tick/transfer or an inventory they acquire themselves. Arbitrary plugin block/shape/neighbor
  callbacks and structure serializers retain their existing tick scope and may have their own lock orders; this
  repair does not make arbitrary plugin locking safe.
- The independent stale-state cases in [block-use-stale-state.md](block-use-stale-state.md) remain unchanged.
