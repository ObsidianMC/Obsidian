# Inventory and world lock order

The block-use transaction follow-up in PR #701 does not repair these inherited lock-order hazards:

- Placement takes the world tick lock and then blocks in `BlockPlacement.LockHand`. A plugin that holds the
  inventory lock while awaiting a world write can hold the opposite lock order and deadlock placement.
- Plugin inventory ticks can hold inventory locks across awaited world writes. They can therefore wait for a
  world tick that is itself waiting for the inventory.

Do not extend these patterns to new use behaviors. Repairing them requires a separate audit of placement,
plugin inventory ticks and their callers; it is not part of the block-use exchange fix.

The use dispatcher takes its hand snapshot with the inventory lock alone, releases it, and then reads the block
under the tick lock. `BlockUse.TryCommit` takes the tick lock and only tries the hand and destination inventories;
it never waits for them. Notifications, features and plugin portal-block factories run after those inventories
unlock. Its new paths do not add a blocking inventory/world acquisition pair.

A follow-up should change placement's blocking hand acquisition to a try-lock with a retry outside the tick lock
(or an explicit refused-commit resync), retaining revalidation when it commits. Releasing the inventory around
inventory-tick hooks would instead weaken their existing atomic view and change the plugin contract. Plugins'
world callbacks still run under the tick lock and must account for the inherited inversion until that repair.
