# Food and harvest

Phase 2 batch A of issue #538 is captured in `food_and_harvest.json`: 99 vanilla 26.3 uses, replayed by
`VanillaFoodHarvest.ReplayFoodAndHarvestReference`. Every case compares the result, swings, food and saturation,
hand and all inventory slots, changed blocks, ordered writes with every flag bit, destructions, drops, sounds,
particles, level events, block events, game events, statistics, criteria and unexpected writes. Sound checks
include pitch, category and exact transmitted position, plus volume and identity. Independent sound-variant
seeds and each item entity's independently seeded initial velocity are not deterministic replay inputs.
Every case also compares exact item drop positions. Bee cases compare occupant counts and each nearby bee's immediate target and
anger state. The four tests in `FoodHarvestTransactions` separately check stale hands, capacity, competing uses
and refusing a busy inventory under the tick lock.

## Captured differences

Only these exact cases differ, each pinned by both hand and inventory in the replay (97 cases match outright):

| Case | Vanilla | Obsidian |
|---|---|---|
| `hive_bottle_creative_full_false` | Two glass bottles become one; one honey bottle enters storage. | Both glass bottles remain; one honey bottle enters storage. |
| `hive_bottle_creative_full_true` | Two glass bottles become one; the full creative inventory discards the honey bottle. | Both glass bottles remain; the full creative inventory discards the honey bottle. |

26.3's hive bottle branch shrinks its input in creative mode. This batch deliberately uses the shared
`BlockUseReturn.Exchange` requested for hive harvests, whose filled-result creative policy retains the input.
Follow up by deciding whether hive harvests need an explicit creative-consumption policy on that helper.

## Vines and smoke: the ambiguous lines resolved

Shears stop cave vine, twisting vine, weeping vine and kelp **heads** at age 25, with one durability cost outside
creative mode. Already mature heads, their plant bodies and wall vines pass without trimming or tool wear.
Berry-bearing cave vines harvest before the shears' item use can run, leaving their age and the shears unchanged.
The existing phase 5 growth dispatcher already observes those ages; no growth implementation was added here.

Smoke checks five cells below the hive against the central 4/16-wide collision post. At the first obstructing
shape, only a lit campfire immediately below that shape qualifies; this can reach a campfire six cells down.
Both campfire types work. Smoke prevents harvest aggression and retains the occupants; it does not clear an
already angry outside bee's target. `safely_harvest_honey` uses this same check through its `smokey` location
predicate on `item_used_on_block`, rather than a separate trigger.

## Bee support and follow-ups

Obsidian already stores hive occupants as `DataBlockEntity` NBT and supports entering, timed release, nectar,
stinging, anger timers, hive/flower positions and bee AI. Harvest now atomically resets honey and detaches
released occupants. Without smoke it targets eligible nearby bees and emergency-releases stored bees, including
through a blocked exit. Released bees within four blocks target the harvester. The immediate captured state has
targets but no running anger timer; the next AI tick starts Obsidian's existing anger timer.

The bounded uses do not establish parity for the subsequent bee lifecycle. In particular:

- `MobSpawner.TickHive` remains the existing timed-release implementation, with simplified weather, day/night,
  elapsed-time and collision rules. Its normal release does not share the new harvest release path.
- Hive `flower_pos` restoration, stay-out-of-hive and ticks-without-nectar countdowns, occupant breeding/love
  progression, and exact later anger/random-draw timing are not implemented by this batch. Age progression on
  emergency release is applied, but only the immediate adult release is captured here.
- Anger time is saved, but its target and the pending first-tick harvest target are not persisted. Bee hive/flower
  positions still use the pre-existing Obsidian NBT fields rather than complete vanilla bee persistence.
- Capture ticking bee/hive lifecycle scenarios before claiming parity for those paths. Dispenser harvests remain
  in PR #729's scope, and cave vine bone meal remains separate from this harvest work.

No existing scenario fixture was re-recorded. Validation for this branch is restricted to affected classes and
their direct neighbours at the user's request; the user will run the full suite before merging.
