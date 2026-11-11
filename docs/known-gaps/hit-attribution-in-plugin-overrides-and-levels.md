# Hit attribution in plugin overrides and levels

**Status:** open, by design for now. Each hit `Living.DamageCombatAsync` deals keeps its own attacker, kind, weapon
and direct source in an async-local scope. The scope is handed to the entity's public `DamageAsync`, which plugin
subclasses may override, and the death the hit causes is handed to `KillAsync` the same way. Only the base
implementation claims a hand-off, once, and only the kind it was handed: damage claims damage, a kill claims a death.
Every other call opens an empty scope (see `Living.HandOff` and `Living.EnterDispatched`). What follows is still
narrower than it could be.

- **Damage started before the base `DamageAsync`.** A plugin override of `DamageAsync` that, before calling `base`,
  starts damage on the same entity (directly, or from a task it starts) lets that damage claim the hit's hand-off. It
  then takes the hit's attacker, kind, weapon and projectile, and the base call made afterwards takes none. A child
  task shares its parent's async-local scope, so nothing tells the two calls apart. Overrides that call `base` first,
  or don't deal damage to the entity themselves, aren't affected.
- **Plugin levels.** In a plugin's own `ILevel` (one travel admits entities to through `TryAddEntity`), a hit lands
  while the entity's `Level` is still that level. A removal there that doesn't go through `RemoveAsync`, and so
  doesn't mark a mob removed, isn't seen; in Obsidian's levels the entity grid tells.
- **Wear before a hit settles.** A hit settles its hurt cooldown under the level's transfer lock, as it commits. Its
  armour, and a wolf's body armour, wear before that, after a first look at the cooldown; vanilla wears them only
  once its cooldown lets the hit through. So a hit that overlaps another on another thread, and that the cooldown then
  takes all of, has already worn them.

**Where to start:** the hand-off would need an identity for the call it was handed to, such as a token the base
`DamageAsync` receives through an internal overload, rather than one shared through the async flow.
