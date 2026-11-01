# Item access beyond containers


**Status:** open. Item access (see [plugin content](../plugin-content.md#item-access)) covers vanilla's containers,
hoppers, droppers and dispensers; the rest of vanilla's item movers aren't modelled.

- **Dispensers** drop every item, like vanilla's default behaviour. Vanilla's item behaviours (shooting arrows and
  other projectiles, pouring and filling buckets, bone meal, placing shulker boxes and other blocks, equipping
  armour, igniting TNT, shearing, ...) aren't modelled, and a dispenser with nothing to fire doesn't emit vanilla's
  `block_activate` game event.
- **Composters** take items put in by hand and by a hopper above, and give bone meal out by hand and to a hopper
  below, adding the layers each item's `minecraft:compostable` gives. Items thrown into one (item entities) aren't
  taken yet, and the ready tick is checked by scheduling and running it directly, not over a 20-tick world run.
- **Minecarts with chests or hoppers** are neither containers hoppers reach (vanilla's `getEntityContainer`) nor
  hoppers themselves.
- **Hopper timing.** A hopper takes in item entities on its own tick, not also as they move into it (vanilla's
  `entityInside`), and takes them in entity id order rather than vanilla's entity section order. Hoppers tick in
  their chunk's block entity order, not vanilla's level-wide list; the cooldown rules make chains independent of
  it, but contraptions that race separate hoppers may differ. The timings are reasoned from the 1.21.11 jar's
  `HopperBlockEntity`, not checked against a vanilla capture.
- **Inside block hooks**, which hold the level's tick lock, a container a player's click holds at that moment, or
  one still waiting for its loot table, takes and gives nothing for that call, where vanilla's single thread never
  meets either case.
- **Fluids** have no access API yet; one would follow `IItemAccess`'s shape (amounts instead of stacks, the same
  simulate flag and sides) when a mod needs it.

**Where to start:** dispenser behaviours go in `DispenserComponent.Fire`, in
`Obsidian/WorldData/Redstone/ItemMovers.cs`, keyed by item like vanilla's `DispenserBlock.DISPENSER_REGISTRY`; a
composter's access is `ComposterItemAccess` (`Obsidian/WorldData/AbstractLevel.Composters.cs`), which
`AbstractLevel.GetItemAccess` returns before it looks for a block entity.
