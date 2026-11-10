# Movement and swing details


**Status:** open.

- **Swings from uses of entities and items in the air.** Since 26.3 the server decides when a use swings an arm: a
  use that succeeds swings it for the players around (`InteractionResult.SUCCESS`) or for everyone
  (`SUCCESS_SERVER`). Uses of blocks do (`UseItemOnPacket`, replayed by `VanillaBlockUse`); uses of entities and of
  items in the air don't track their results yet, so they show no swing.
- **Stepped movement.** Vanilla sends living entities' per-tick positions between updates as stepped `VecDelta`s and
  `PositionPath`s. Obsidian sends one linear move per update, so a mob's path between updates is a straight line.
- **Moves during a teleport.** Vanilla ignores a player's moves until the client accepts the teleport it was sent.
  Obsidian applies them as they come.

**Where to start:** have the entity interactions in `MainEventHandler` and the item uses in `ItemUseBehaviors`
report vanilla's result, and swing as `Living.SwingForUse` does for blocks; record a per-tick step history on `Mob` for `EntityMoveDelta`'s
stepped forms.
