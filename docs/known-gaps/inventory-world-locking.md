# Inventory and world lock order

The block-use transaction follow-up in PR #701 does not repair these inherited lock-order hazards:

- Placement takes the world tick lock and then blocks in `BlockPlacement.LockHand`. A plugin that holds the
  inventory lock while awaiting a world write can hold the opposite lock order and deadlock placement.
- Plugin inventory ticks can hold inventory locks across awaited world writes. They can therefore wait for a
  world tick that is itself waiting for the inventory.

Do not extend these patterns to new use behaviors. Repairing them requires a separate audit of placement,
plugin inventory ticks and their callers; it is not part of the block-use exchange fix.
