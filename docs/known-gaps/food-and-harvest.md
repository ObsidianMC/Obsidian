# Food and harvest

Phase 2 batch A of issue #538 is captured in `food_and_harvest.json`: 123 vanilla 26.3 uses, replayed by
`VanillaFoodHarvest.ReplayFoodAndHarvestReference`. Every case compares the result, swings, food and saturation,
hand and all inventory slots, changed blocks, ordered writes with every flag bit, destructions, drops, sounds,
particles, level events, block events, game events, statistics, criteria and unexpected writes. Sound checks
include pitch, category and exact transmitted position, plus volume and identity. Independent sound-variant
seeds and each item entity's independently seeded constructor velocity are not deterministic replay inputs.
Every case compares exact item drop positions; pumpkin cases also compare all three deterministic motion components.
Bee cases compare occupant counts and nearby bees' immediate targets and anger. The seven tests in
`FoodHarvestTransactions` check stale hands, capacity, competing uses, busy inventories, concurrent timed/harvest
release (including the saved occupant list), delayed honey writes after a harvest or hive replacement, and rejected
timed admissions.

## Captured parity

All 123 cases match their recorded fields without pins. Hives consume a glass bottle even in creative mode via
`BlockUseReturn.Exchange(..., consumeInCreative: true)`. That option defaults to false, preserving bucket and
cauldron callers, including #730's separate `limitCreative` rule for washed items. Creative single bottles replace
the hand; stacked bottles return honey to storage or discard it when the creative inventory is full.

## Review regressions

- `TimedReleaseAndHarvestDetachEachStoredBeeOnce` uses explicit detach/resume signals to run harvest between
  the real timed release stages of a hive that began with two stored bees. It checks two live bees, no stored bees,
  the saved empty occupant list and exactly one honey bottle. Entry, timed release and harvest mutate occupants under the same tick lock. The timer
  finishes detaching before spawning and never writes its old snapshot back afterwards. The test has no polling,
  timed lock attempts or wait deadlines; only its fact timeout guards a hang. The busy inventory test likewise
  uses only its fact timeout.
- `FailedTimedAdmissionDoesNotLoseOtherStoredBees` forces a live UUID clash for the first of two stored bees.
  The unique second bee is admitted, while the rejected occupant is appended to a fresh snapshot under the tick
  lock. Retrying does not duplicate a bee or credit nectar from the rejected occupant, and saving retains it.
  Entity admission happens outside that lock; only successful admissions queue a honey delivery.
- `QueuedNectarCannotRefillAHarvestedOrReplacedHive` checks a valid queued delivery, a harvest before queue drain,
  and replacement by a new hive with the same original state. Queued honey writes require both the original
  resident block entity and the state they were computed from.
- `hive_bee_box_corner`, `hive_bee_box_overlap` and `hive_bee_outside_box` capture vanilla's inflated hive box,
  including body overlap across its boundary. Loaded bees are checked directly against the exact box using
  their current dimensions, without a spherical cutoff that could exclude a corner or a scaled bee.
- `shipped_safe_honey_smoked` and `shipped_safe_honey_unsmoked` read the real advancement JSON from vanilla's
  resources. Replay also checks those captured conditions against Obsidian's shipped definition. The four
  `hive_codec_any_*` cases cover nested `all_of`/`any_of`, either matching branch, failure and an empty disjunction.
- Five `hive_codec_single_*` cases capture object-form `terms` in `all_of` and `any_of`, including nested
  success and failure. Seven `hive_codec_default*` cases capture omitted tool/location predicates, offset handling
  and composition. `cake_codec_default_tool_empty_hand` checks an omitted tool predicate with an empty hand.
- Every pumpkin carve capture now checks its level-random seed velocity, including all six clicked faces,
  creative mode, tool breakage and offhand use.

## Shipped advancement predicate forms

Sixteen shipped 26.3 advancements contain object-form `location` predicates. Besides `safely_harvest_honey`, the
other composite users are:

- `adventure/heart_transplanter` (`any_of`, plus direct `match_block` criteria), `adventure/lighten_up`,
  `adventure/play_jukebox_in_meadows`, `adventure/read_power_of_chiseled_bookshelf` (`all_of` and `any_of`),
  `adventure/revaulting`, `adventure/under_lock_and_key`, and `adventure/use_lodestone`.
- `husbandry/allay_deliver_cake_to_note_block`, `husbandry/make_a_sign_glow`, `husbandry/wax_off`,
  `husbandry/wax_on`, and `nether/charge_respawn_anchor`.

`husbandry/place_dried_ghast_in_water`, `husbandry/plant_any_sniffer_seed` and `husbandry/plant_seed` use direct
object-form `match_block` predicates. Matching now accepts the codec object forms and their nested terms, while
retaining legacy fixture arrays; location checks also respect their integer offsets. This fixes decoding and
composition, not every unsupported inner location predicate or unrelated advancement trigger.

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

- Normal timed release still has simplified weather, day/night, elapsed-time and collision rules. It now shares
  occupant serialization with harvest and entry, but their release policies remain distinct. A timed occupant
  whose entity admission is rejected stays stored for retry; vanilla discards a duplicate UUID instead.
- Breaking an occupied hive still loses its stored bees. This pre-existing break-path gap is outside this round.
- Hive `flower_pos` restoration, stay-out-of-hive and ticks-without-nectar countdowns, occupant breeding/love
  progression, and exact later anger/random-draw timing are not implemented by this batch. Age progression on
  emergency release is applied, but only the immediate adult release is captured here.
- Anger time is saved, but its target and the pending first-tick harvest target are not persisted. Bee hive/flower
  positions still use the pre-existing Obsidian NBT fields rather than complete vanilla bee persistence.
- Capture ticking bee/hive lifecycle scenarios before claiming parity for those paths. Dispenser harvests are
  handled by #729, and cave vine bone meal remains separate from this harvest work.

Only `food_and_harvest.json` was re-recorded. Validation for this branch is restricted to affected classes and
their direct neighbours at the user's request; the user will run the full suite before merging.
