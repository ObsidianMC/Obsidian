# Cauldrons

**Status:** partial. Using a cauldron works as vanilla's `CauldronInteractions` table does, checked against vanilla by
`cauldrons.json` (see [scenario captures](../scenario-captures.md)): water, lava and powder snow buckets filling and
emptying each kind of cauldron, lava and powder snow refused under water, water bottles and glass bottles changing
the level, and a water cauldron washing dyed leather and wolf armor, banners' last pattern and dyed shulker boxes,
with their statistics, criteria, sounds and game events, from either hand, in each game mode, and with a stacked
hand or a full inventory. Rain and snow already fill cauldrons (`melting_weather.json`), and so do pointed dripstone
drips (`ticking_blocks.json`). What's left:

- **Entities inside cauldrons.** Vanilla's water and powder snow cauldrons put out a burning entity inside them, and
  lose a level doing it (a powder snow cauldron turns into a water cauldron a level lower); a lava cauldron sets an
  entity on fire and hurts it like lava, and thaws a freezing one. The contact pass binds `entityInside`, but none of
  this is implemented: vanilla applies these as inside-block effects (`InsideBlockEffectApplier`), each at most once
  a tick and in a fixed order, so a mob in lava and a lava cauldron at once burns once, and Obsidian has no
  counterpart. It doesn't put out a burning entity in water either. The block contact capture records neither fire
  nor health yet, so it has to grow those first. Phase 3 of the
  [block interactions plan](../block-interactions-plan.md) owns this, with powder snow's fire handling.
- **Dispensers using cauldrons.** A dispenser's buckets and bottles don't fill or empty a cauldron in front of it.
  Phase 4 of the plan, with the other dispenser behaviours.
- **Effect encoding.** A water bottle with a custom effect is refused, as vanilla's is, but Obsidian writes the effect
  without the default `show_icon` vanilla's codec adds, so `empty_water_bottle_custom_effect` and
  `water1_water_bottle_custom_effect` pin the hand and inventory. The gap is in item encoding, not cauldrons.

Not compared by the capture: sound pitches and seeds.
