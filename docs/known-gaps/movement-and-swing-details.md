# Movement and swing details


**Status:** open.

- **Swings from uses.** Since 26.3 the server decides when a use swings an arm: a use that succeeds swings it for
  the players around (`InteractionResult.SUCCESS`) or for everyone (`SUCCESS_SERVER`). Obsidian doesn't track its
  uses' results, so only attack swings (`punch`) and mobs' attacks are shown to other players.
- **Stepped movement.** Vanilla sends living entities' per-tick positions between updates as stepped `VecDelta`s and
  `PositionPath`s. Obsidian sends one linear move per update, so a mob's path between updates is a straight line.
- **Moves during a teleport.** Vanilla ignores a player's moves until the client accepts the teleport it was sent.
  Obsidian applies them as they come.

**Where to start:** have `ItemUseOnBehaviors` and the entity interactions in `MainEventHandler` report vanilla's
result, and swing as `Living.SwingForAttack` does; record a per-tick step history on `Mob` for `EntityMoveDelta`'s
stepped forms.
