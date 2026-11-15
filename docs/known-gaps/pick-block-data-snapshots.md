# Pick-block data snapshots

**Status:** Picking captures a block and its block-entity identity under the level's tick lock. Data-backed block
entities (`DataBlockEntity`, including beehives) also copy their saved NBT in that same hold, so a hive's honey state
cannot be combined with occupants from a later harvest.

Other save paths still run after the snapshot locks are released. In particular, `ContainerBlockEntity.Save` and
`SnapshotContents` take the inventory snapshot/click lock and loot lock. Taking those while holding `TransferLock`
would invert the tick -> container click -> transfer lock order. A concurrent container change or removal can therefore
change or empty the contents before pick-block serializes them. Other native save paths have not been certified as
inventory-lock-free here, and plugin component collection also runs outside the snapshot hold.

Making these remaining data reads atomic needs inventory admission or revalidation in the established lock order.
That coordination is intentionally not added by the small pick-block fix.
