# Movement and swing details


**Status:** open.

- **Swings from uses of entities and items in the air.** Since 26.3 the server decides when a use swings an arm: a
  use that succeeds swings it for the players around (`InteractionResult.SUCCESS`) or for everyone
  (`SUCCESS_SERVER`). Uses of blocks do (`UseItemOnPacket`, replayed by `VanillaBlockUse`); uses of entities and of
  items in the air don't track their results yet, so they show no swing.
- **Stepped movement.** Vanilla sends living entities' per-tick positions between updates as stepped `VecDelta`s and
  `PositionPath`s. Obsidian sends one linear move per update, so a mob's path between updates is a straight line.
- **Snow lifting a player.** Snow that thickens under an entity lifts it by the growth in the snow's height
  (`Entity.LiftForBlockChange`), and a player is sent the new position as a teleport. Because moves during a teleport
  are still applied (above), a move the client sent before it saw the lift puts the player back at the old height.
  The lift doesn't look for a ceiling, and a rider and its vehicle are lifted independently; what vanilla does in
  those cases isn't captured.
- **Other position writers racing travel.** Serverbound player movement commits, boat client-movement commits and
  player passenger positioning now recheck teleport state at commitment. This is not a guarantee for every entity
  position write. In particular:
  - A moving piston computes a displacement and calls `Entity.UpdatePositionAsync`. Its chunk lookup can await;
    a server teleport can then commit a newer destination before the lookup resumes and writes the older position.
    Both overloads of `UpdatePositionAsync` lack a generation carried from the displacement's calculation.
  - A custom mob's `PluginMob.Travel` reads `ClientVehicleControl.Accepted`, then returns a position for `Mob.AI` to
    commit later. A server teleport of the mob can occur between that snapshot and the commit. The check on the
    accepted snapshot does not make that later transform atomic with travel. Boats close this particular gap with
    an owning-level transfer lock around accepted-move validation and commit; custom-mob travel needs a separate
    change to its deferred transform pipeline.
  These races predate the pending-teleport rule. Fixing them needs generation-aware displacement/transform commits
  across those callers; it must not hold locks across chunk awaits or virtual/plugin code, or add movement limits.

**Where to start:** have the entity interactions in `MainEventHandler` and the item uses in `ItemUseBehaviors`
report vanilla's result, and swing as `Living.SwingForUse` does for blocks; record a per-tick step history on `Mob` for `EntityMoveDelta`'s
stepped forms.
