# Projectile block hits

**Status:** partial. Every projectile that hits a block goes through one path (`ProjectileBlockHits`), at the point
of its tick where vanilla's `onHitBlock` runs: the block reacts, then the projectile's own block reaction, then
`projectile_land`, then what the projectile does on any hit. Target blocks (power, the `target_hit` statistic and
criterion), campfires, candles and candle cakes lit by burning projectiles, TNT primed by them, chorus flowers and
pointed dripstone or sulfur spikes broken by projectiles, Channeling's lightning on a lightning rod and wooden buttons
pressed by arrows are checked against vanilla by `projectile_block_hits.json` (see
[scenario captures](../scenario-captures.md)). `VanillaProjectileHits` pins each difference below by case and field,
and fails once one goes.

## Differences the replay pins

- **The bolt Channeling calls down doesn't strike.** The hit summons the lightning bolt and plays the trident's
  thunder, as vanilla's `hit_block` enchantment effect does, but Obsidian has no lightning bolt behaviour: the
  lightning rod isn't powered and doesn't spark, there's no `lightning_strike` game event, nothing is set alight or
  struck, and the bolt stays until its chunk unloads. Lightning rods giving power is phase 4 of the
  [block interactions plan](../block-interactions-plan.md); a lightning bolt entity is where to start
  (`channeling_trident_lightning_rod`).
- **Arrows press wooden buttons after they hit.** Vanilla's arrow presses a button it moves through during its move,
  before its own hit, so the click and `block_activate` come before the arrow's sound and `projectile_land`. Obsidian
  presses it in the contact pass after the arrow's tick. Phase 3's one contact pass is where that changes
  (`arrow_oak_button`, `spectral_arrow_oak_button`, `trident_oak_button`).
- **A splash potion's particles have a fixed colour.** Obsidian has no effect colours to mix the potion's from
  (`splash_potion_target`, `splash_potion_chorus_flower`).
- **Water doesn't put out a burning arrow,** so vanilla's extinguish sound as a flaming arrow enters a waterlogged
  campfire is missing (`flaming_arrow_waterlogged_campfire`).
- **Experience orbs don't emit `hit_ground`** as they land (`experience_bottle_target`).
- **Shulker bullets fly as arrows.** Obsidian's bullet stops at the block it hits; vanilla's moves on into it before
  it's destroyed, so its `entity_damage` comes from a block lower (`shulker_bullet_target`).
- **A grounded bobber rests.** Vanilla's bobber, lying on the block it hit, hits it again on its second and fourth
  ticks (a zero-length hit test on its surface), counting `target_hit` each time; Obsidian's rests
  (`fishing_bobber_target`).

## Not captured

- **Projectiles Obsidian lacks.** A player's wind charge is a plain entity with no flight or burst, and lingering
  potions don't exist, so neither reaches the path.
- **Firework rockets** don't take the path: their flight ends on any collision.
- **Projectile flight.** The cases start each projectile just above the block, falling straight at it, so that the hit
  lands on the same tick on both sides. Obsidian moves thrown and hurting projectiles with gravity and drag after the
  move, where vanilla applies them before; longer flights can land elsewhere or later.
- **Explosion fire.** With `mob_griefing` on, a ghast's fireball sets one fire where it lands instead of vanilla's
  explosion fires. The cases run with it off, as explosions' random drops would otherwise differ.
- **Projectiles hitting entities** keep their own paths, which this change didn't touch.
