# Inventory and world lock order

## Lock graph (audited from 975ae61a3, before the placement repair)

An arrow means an acquisition while holding the left side. `wait` includes synchronous blocking and asynchronous
waiting; `try` means `Wait(0)`, releasing any partial acquisition on failure.

| Path / lock | Edges and acquisition behaviour |
| --- | --- |
| Level simulation gate | `simulationGate` serializes ticks, saves and unloads (async wait). It encloses player tick gates and synchronous tick-lock sections. Code holding a tick lock, transfer lock or player tick gate must not wait for a simulation gate (`MustNotWaitForGates`). |
| Player tick gate | `tickGate` serializes ticks, grants, saves and leaving (async wait, own-flow reuse). Item ticking takes `tickGate -> player inventory` (wait). Queued block use only tries the player gate; cross-player gate waits are prohibited. Ordinary incoming placement does not take this gate. |
| Inventories and windows | Each `BaseContainer.TransactionLock` is a non-reentrant semaphore. `Lock` waits synchronously, `LockAsync` asynchronously, `TryLock` only tries. Composite inventories and `LockWindowAsync` are sets of these same locks, **not a separate window mutex**: distinct parts acquire in stable `LockRank` order and release in reverse. Separate nested acquisitions do not gain that ordering automatically. |
| Inventory-tick hooks | `player inventory -> plugin InventoryTickAsync -> tick lock` (wait for a world write). Inventory stays locked across the hook's awaits, preserving its atomic view. Deferred damage/death inventory work runs after unlock. Click/menu plugin callbacks can also write the world while owning ranked inventory parts. |
| Placement (defect) | `tick lock -> BlockPlacement.LockHand -> player inventory` (wait). Preparation, item consumption, block write and placement effects are inside both locks. This closes the cycle with inventory-tick hooks. |
| Block use (#701) | Hand snapshot owns only the inventory (async wait), releases it, then reads under the tick lock. `BlockUse.TryCommit` takes `tick -> hand inventory -> destination inventory` using **try** for both inventories, revalidates, and commits synchronously. Its effects scope publishes notifications, features and portal factories after both inventories unwind (still under tick). This adds no inventory wait edge and does not repair placement's older edge. |
| Furnace / campfire | `TickFurnaceAsync`: `tick -> furnace inventory` (wait). `UseCampfireAsync`: `tick -> player inventory -> campfire inventory` (wait). `TickCampfireAsync`: `tick -> campfire inventory` (wait). These are also inversions; a busy tick can be skipped and a busy insertion refused without partial work. |
| Container item access | Under tick, `ContainerItemAccess.Enter` tries its inventory semaphore; otherwise it waits. Vehicle item access always tries. Protected `ContainerBlockEntity.LockInventory` refuses under tick; outside tick it waits and forbids other locks/awaits. Item access prepares pending loot before inventory acquisition, or queues it if tick/transfer is held. |
| Entity transfer | `inventory and/or tick -> TransferLock -> chunk EntityLock -> entity grid`. Transfer acquisition is synchronous; never hold two levels' transfer locks, await, load chunks, or run plugin code inside one. Pickup holds inventory before transfer; hopper access under transfer must not wait for inventories. |
| Chunk and lighting | Live writes take `tick -> Chunk.BlockLock`; runtime lighting also uses `tick -> propagation` (reader/writer lock). Saves acquire generation's chunk lock before tick, then block snapshots and entity snapshots. Chunk loading/region lookup and I/O must finish outside the write's tick section. `regionLock` is not acquired inside that section. |
| Notification queue | Writes take `tick -> BlockNotificationPipeline.sync` (short blocking monitor). Draining takes `sync` only to dequeue/change bookkeeping, releases it before world reads, hooks and awaits. There is no `sync -> tick/inventory` edge. Placement already invokes synchronous world callbacks while holding its hand; this repair must not expand that scope. |
| Other bookkeeping | Container viewer-list monitor is short and releases before callbacks. `WindowSnapshots.gate` serializes currentness checks and packet submission after inventory release and plugin packet handlers. Player-grid membership has a short monitor for joins, leaves and query snapshots; it does not wait for inventories. Container loot uses `LootGenerationLock -> inventory -> LootLock`; loot preparation runs outside tick/transfer/click locks. Container save and component application take `inventory -> LootLock`; data block entities also have their data monitor. Plugin call leases are nonblocking entry/lifetime accounting, not inventory mutexes; live block-entity calls are awaited before removal outside tick. |

## Rule

**Blocking acquisition order is inventory before world tick. A tick-lock holder must never wait for an inventory:
try every required inventory and abandon/defer the operation if any is busy.** This also applies to nested world
callbacks and composite inventories. Taking tick then inventory by try-lock is permitted because it adds no wait
edge. Never await inventory acquisition under tick, or retry while holding tick. Do not acquire a second inventory
by waiting while holding the first unless both belong to one ranked acquisition set.

Keeping inventory-tick hooks atomic permits their existing awaited world writes. Deferring those writes changes
when plugins observe them; unlocking around hooks changes their inventory contract. Removing the opposite wait
edge is the smaller compatible repair.

## Placement repair

Placement now tries the hand **once**, before validation, preparation or writes. Contention returns false without
changing the block or consuming an item. There is no automatic retry and no spin: the player sees a refused click,
with the clicked cell, adjacent cell and hand resent by the existing `UseItemOnPacket` completion path before the
acknowledgement. Another player click can try again. Revalidation, block publication and exactly one consumption
still share the tick and hand locks. Cancellation and actor/hand changes before commit still refuse the operation.
No plugin callback or await gains a new held lock. Furnace and campfire ticks likewise try once and skip a busy
tick (normal ticking tries next tick); campfire insertion tries both inventories and refuses before any change.

`PlacementInventoryLocking.InventoryTickWorldWriteAndPlacementCannotDeadlockEachOther` runs the real plugin
inventory-tick hook and placement with explicit rendezvous points while each owns its first lock. On the original
code, its ten-second bound failed with `placement completed=False, item tick completed=False`. A test-only
cancellation escape released the hook, and both pending writes were joined before disposal. After the repair,
placement refuses and the hook's world write completes while its inventory view remains unchanged. No sleeps or
scheduling races decide the result. The probe callback lives only in the test plugin; production needs no test seam.

## Other violations and boundaries

- `Region.SnapshotChunkAsync -> SerializeChunk -> BlockEntityNbt.Save -> ContainerBlockEntity.Save` /
  `SnapshotContents` waits for resident container inventories under tick. `BlockEntityLifecycle.Retire` also waits
  for each inventory under tick on unload. These require coordinated snapshot/retirement admission before tick,
  with resident identity revalidation and all-or-nothing unload handling; merely skipping would lose save data.
- The same snapshot calls `CollectEntities -> EntityNbt.Save -> VehicleInventory.Save` for chest boats and
  container minecarts: `tick -> transfer -> chunk EntityLock -> vehicle inventory` (wait). This also violates the
  transfer lock's inventory ordering. Entity snapshot admission must be fixed together; skipping loses entities.
- `PluginBlockEntities.Keep/KeepCore` waits for container retirement and saving under tick, including placement's
  `CompleteBlockEntityPlacement -> KeepIfUnloading`. Plugin preservation needs coordinated admission and a
  retryable unload sweep; skipping would leave a live plugin instance behind. Keep this lifecycle work separate.
- Placement's `BlockEntityComponents.ApplyContainer` locks a newly constructed, unpublished entity's inventory
  under tick and hand. Normally uncontended, but a plugin factory that publishes/reuses an inventory can make it
  block. Moving preparation out requires managing plugin factory/component effects and lifetime; record rather
  than silently skip transferred contents. The same factory concern applies to creation/loading under tick.
- `CampfireBlockEntity.GetUpdateData` waits for its inventory. Normal send/update processing runs outside tick;
  a caller or plugin invoking update-data construction under tick would violate the same rule. Container save APIs
  likewise remain blocking APIs and must not be called under tick outside the recorded snapshot work.
- Arbitrary plugin block/shape/neighbor/factory callbacks already run under tick and may use other blocking APIs
  or raw locks. This audit fixes engine acquisition sites, not arbitrary plugin lock orders. No extra lock may be
  introduced across such callbacks.

The stale-state cases in [block-use-stale-state.md](block-use-stale-state.md) are independent of this lock order.
