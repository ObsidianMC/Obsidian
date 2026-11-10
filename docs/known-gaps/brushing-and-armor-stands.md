# Brushing and armor stands

**Status:** partial. Brushing suspicious sand and gravel and using armor stands work and are checked against vanilla
by `brushing.json` and `armor_stand_use.json` (see [scenario captures](../scenario-captures.md)). What's left:

## Brushing

- **Item entities and falling blocks don't emit `hit_ground`.** Vanilla's item entity, landing on the sand a brushed
  block became, and a falling suspicious block landing before it breaks, both emit `minecraft:hit_ground`. Obsidian's
  item entities and falling blocks emit nothing when they land. The brushing replay pins exactly those two events
  (`sand_top_completes_with_loot` tick 101, `falls_after_a_stroke` tick 19) and fails once either is sent. Fixing it
  belongs to entity movement's game events, not to brushing.
- **Entities in the brush's way.** Vanilla aims with `ProjectileUtil.getHitResultOnViewVector`, which also hits
  pickable entities (an armor stand, a falling block, another player) and lets go of the use when it does.
  `PlayerBase.BrushTarget` only clips block collision shapes, so a brush keeps stroking the block behind an entity.
  No case covers it.
- **A busy inventory skips a stroke.** A stroke changes the block and wears the brush under the level's tick lock
  with the player's inventory tried, never waited for (see [inventory and world locking](inventory-world-locking.md)).
  When the inventory is busy (a click being applied at that moment), the whole stroke is skipped, sound included,
  and the next one ten ticks later tries again. Vanilla has no such case. The same applies when, between rolling the
  block's loot (outside the locks, since a plugin's table could load chunks) and the stroke, the table changed, the
  block or its block entity was replaced, or the brush's count or components changed.
- **Another brush in the same hand.** Vanilla's `updatingUsingItem` keeps a use going when the hand holds another stack
  of the same item, so swapping one brush for another mid-brush carries on with the new one. Obsidian's held use
  follows the stack it started with and stops when it's replaced. Not captured.
- **The use's start and end game events for other items.** `item_interact_start` and `item_interact_finish`, which
  vanilla emits for every item whose `use_effects` allow it (bows, shields, food, spyglasses), are emitted for brushes
  only.
- **Container loot doesn't fire `player_generates_container_loot`.** Brushing fires it with the loot table rolled;
  chests, barrels and chest vehicles opening their loot don't yet.
- **Loot rolled from the level's sequence.** A brushable block with no `LootTableSeed` rolls from the level's random
  sequence for its table, as vanilla does, but the capture only uses fixed seeds: the sequence depends on the world
  seed, which the capture's world and the replay's don't share.
- **Rotation lag.** Vanilla aims with the last tick's head rotation (`getViewVector(0)`); Obsidian aims with the current
  one. The probe sets both to the same value, so the capture can't show the difference; a client turning between
  ticks could see a stroke land one tick earlier on the new target than vanilla.

## Armor stands

- **Hitting and breaking stands.** Only using a stand is modelled on `ArmorStand`: hitting it (the wobble, two hits to
  break it, creative breaking), dropping what it wears and holds, and fire and explosions breaking it aren't, and
  haven't been checked against vanilla.
- **Other entities' interactions.** The `player_interacted_with_entity` trigger and the swing vanilla's
  `handleInteract` gives a successful use are given for armor stands only. Every other entity's use still emits
  `entity_interact` at the click, which vanilla doesn't do for armor stands (the replay checks they don't); whether it
  does for the others hasn't been captured.
- **Equipment packets come sooner.** Vanilla sends a stand's changed equipment from its next tick; Obsidian sends the
  changed slot when the use commits. The packets are the same.
- **The `scale` attribute.** Vanilla divides the clicked height by the stand's scale times its age scale; Obsidian uses
  the age scale (half for a small stand) only, so a stand given another `scale` picks its slots as if it had none.
- **Renaming with a name tag.** A name tag passes the stand's own use, as vanilla's does, and goes to the held item's
  use on a living entity, whose renaming isn't covered by this capture.
