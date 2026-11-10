# Inventory and world lock order

Audited from 18321fecd, after placement #715, block entity ticking #711, pending teleports #712 and furnace recipe
awards #716. The original graph and placement reproduction were committed before #715's implementation. This
follow-up removes the snapshot wait edges and adds one bounded placement retry.

## Rule and graph

**Wait for inventories only outside world tick, entity transfer and chunk entity/generation locks. Under those
locks, try every required inventory; on contention release everything and defer or retry without those locks.**
Release inventory after a retry's availability wait, before re-entering tick. Several inventories acquired by waiting
belong to one ranked set; separate nested acquisitions do not inherit that ordering. Ordinary inventory locks are
non-reentrant. Host snapshot serialization alone reuses the inventories its explicit admission already owns.

An arrow means acquisition while holding the left side. `wait` includes synchronous and asynchronous waiting;
`try` means `Wait(0)`, abandoning partial acquisitions on failure.

| Path / lock | Edges and acquisition behaviour |
| --- | --- |
| Level simulation gate | Serializes ticks, saves and unloads (async wait), enclosing player gates and tick sections. Code holding tick, transfer or a player gate must not wait for simulation gates (`MustNotWaitForGates`). |
| Player tick gate | `tickGate -> player inventory` (wait) for item ticks, grants and saves, with own-flow reuse. Queued block uses only try the player gate. Cross-player gate waits are prohibited. Incoming placement does not acquire it. |
| Inventories / windows | Each `BaseContainer.TransactionLock` is a semaphore. `Lock` waits synchronously, `LockAsync` asynchronously, `TryLock` does not wait. `LockWindowAsync` and composites acquire distinct inventory parts in stable `LockRank` order and release in reverse; there is no additional window mutex. |
| Item hooks / menu callbacks | Inventory-tick hooks keep `inventory -> plugin InventoryTickAsync -> tick` (wait) for an awaited world write. Their atomic inventory view is unchanged. Deferred damage/death work follows unlock. Menu callbacks can also own inventory while writing the world. |
| Placement | `tick -> hand` (try), revalidating and committing the block and consumption together. Contention gets at most one availability wait outside tick, then another `tick -> hand` try. An inherited tick/transfer lock disables the wait. |
| Block use (#701) | Hand snapshot waits with inventory alone, releases it, then reads under tick. `BlockUse.TryCommit` takes `tick -> hand -> destination` with try-locks for both inventories. Effects publish after both inventories unwind, still under tick. |
| Furnace / campfire | Tick operations try their machine inventory and skip busy ticks. Campfire insertion tries hand then campfire and refuses before changing either if busy. Campfire update data tries once, retains its last complete client projection and queues another update if busy. |
| Container item access | Under tick, `ContainerItemAccess.Enter` tries inventory; otherwise it waits. Vehicle access always tries. Protected `LockInventory` refuses under tick and forbids awaits/other locks in its scope. Loot preparation finishes outside tick/transfer/click locks, or is queued. |
| Entity transfer | Pickup owns hand before waiting for transfer. Transfer precedes chunk `EntityLock` and grid locks. Never hold two levels' transfer locks, await, load chunks or invoke plugin code under transfer. |
| Region snapshots | Generator lock -> tick -> transfer -> all required inventories (try). Admission includes block containers and vehicles in the chunk. Host serializers and retirement reuse admitted inventories; entity serialization also takes chunk `EntityLock`. A failed admission takes no dirty marks, retires nothing and removes no entities. |
| Plugin preservation | Final plugin Save runs without tick or new inventory locks. Host preservation takes tick -> inventory admission (try), revalidates identity, freezes the inventory and saves its current host-owned contents. A call inheriting tick, or a busy host admission, queues preservation and retains an unloading hold. |
| Chunks / lighting / structures | Live writes take tick -> `Chunk.BlockLock`; runtime light uses tick -> propagation reader/writer lock. Chunk I/O and region lookup finish before write sections. Structure-state serializers may call plugins: they finish in their existing tick scope before snapshot admission adds transfer/inventory locks. |
| Notification queue | Tick -> `BlockNotificationPipeline.sync` is a short blocking monitor. Drain releases `sync` before world reads, hooks and awaits; it adds no reverse tick/inventory edge. Placement's existing synchronous callbacks retain their existing lock scopes. |
| Other bookkeeping | Viewer-list, player-grid membership and `WindowSnapshots.gate` are short monitors; callbacks/packet handlers run outside them. Loot uses `LootGenerationLock -> inventory -> LootLock`; saves/components use inventory -> LootLock. Data entities and attachments have their data monitors. Plugin call leases are nonblocking lifetime accounting. #716's `recipeEventsSync` is taken under furnace/window inventory or the player's tick gate, and released before awaits, criteria delivery or other lock acquisition. |

## Snapshot admission and busy data

The inherited vehicle cycle needed no plugin: a ranked window acquired vehicle inventory and waited for the hand;
pickup owned the hand and waited for transfer; a region snapshot owned transfer and chunk EntityLock and waited
for vehicle inventory. The bounded rendezvous test failed on the original code with
`save=False, window=False, pickup=False`; cleanup released the test-held hand and joined all pending operations.
After the fix the snapshot abandons inventory admission, freeing transfer for pickup and the window to finish.

`InventorySnapshot` admits the complete inventory set without waiting, before dirty-state hashing, retirement or
serialization. All those host operations reuse admission on the same synchronous thread; general inventory locks
do not become reentrant. No plugin Save override, structure serializer or await runs in that scope. This is save
work only, not a new lock per block entity per tick.
Structure-serializer failures are captured before admission and rethrown at the original post-retirement write
point, preserving unload-failure cleanup without adding inventory/transfer locks around plugin code.

- An eager save skips the **whole chunk**, retaining its unsaved state and entities for a later save.
- An explicit save or unload releases generator, tick, transfer and inventory locks, awaits the busy inventory's
  availability, releases it, then retries admission against current chunk and entity membership. It reports success
  only after taking a complete current snapshot. It never writes an omitted inventory or a cached stale inventory.
  A caller already owning tick/transfer cannot unwind its outer lock: a busy explicit snapshot throws without
  changing data, leaving it for a legal save; an eager attempt still skips it.
- Unload retires all containers only after the entire set is admitted. A busy later container cannot leave earlier
  containers retired. The final snapshot freezes their contents together with entity removal before detaching.
- Plugin preservation leaves a busy entity resident and its inventory unretired. Deferred work keeps its plugin
  alive, tries again on a later entity-action drain, revalidates identity, then closes retired windows. The final
  plugin Save runs outside tick; host contents are overlaid under admission, so plugin saved data cannot erase
  a later inventory edit. Placement's late-unloading preservation defers the entire operation.
- A busy campfire client projection keeps the previous complete projection and requests another update. Its disk
  snapshot follows normal admission and always contains its current inventory.

The regression tests read committed chunk/entity payloads before server disposal can perform another save, then
load those exact NBT bytes through a separate region. They verify final item identities/counts and absence of extra
stacks for the vehicle cycle, vanilla and plugin-style container snapshots, unload retirement, preservation, and
campfire projection. Tests use explicit signals and bounded cleanup, not sleeps or statistical scheduling.

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
  discard an entity that neither level accepted. This is separate from recurring region snapshots. The generic
  `Region.Evict(keepSaved: true)` serializer has the same boundary; its normal plugin-unload caller only keeps
  plugin-owned entity types (built-in vehicles spawned by a plugin use `keepSaved: false`).
- Factory preparation still calls `BlockEntityComponents.ApplyContainer` on an unpublished inventory under tick
  and hand. Normally uncontended, a plugin factory publishing/reusing an inventory can make it block. Moving
  factory/component preparation requires managing plugin effects and lifetime; skipping transferred contents
  would lose items. Creation/loading and factory update-data callbacks need the same separate design.
- Standalone container Save/SnapshotContents APIs still block outside host admission. Plugins must not call those
  APIs while owning tick/transfer or an inventory they acquire themselves. Arbitrary plugin block/shape/neighbor
  callbacks and structure serializers retain their existing tick scope and may have their own lock orders; this
  repair does not make arbitrary plugin locking safe.
- The independent stale-state cases in [block-use-stale-state.md](block-use-stale-state.md) remain unchanged.
