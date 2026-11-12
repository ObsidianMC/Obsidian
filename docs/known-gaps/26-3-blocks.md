# 26.3 blocks


**Status:** Phase 6 step 1's sulfur slice and construction/music slice, and the block interactions plan's batch G
(shelves, straw beds' sleep rules, golden dandelions' age locking), are implemented with the limits below.
`VanillaMoreBlocks.ReplayConstructionAndMusicReference` replays `more_blocks.json` as one test with labelled
failures across every case. `MoreBlockFixtures.java`, registered in `FixtureDumper.cs`, records it through
`CaptureFixtures=more_blocks`.

Poplar logs/wood and stripped variants, planks, stairs, slabs, fences, gates, doors, trapdoors, buttons, pressure
plates and signs use the existing block-family APIs. The reference checks axis placement, axe stripping (state,
sound ID and durability cost), door upper halves, opening/closing states and sound IDs, powered placement, gate
wall placement and fence connections. Standing and ceiling sign probes start with the variant already selected;
sign item selection and editing still use the shared sign implementation. The shelf's basic placement is covered
here; its storage, swapping and connections are below.

All 16 colors of wool and concrete stairs/slabs have captured placement, waterlogging, slab merging and stair
corner updates/placement. Red, orange and yellow poplar leaves have captured persistent placement, waterlogging,
live distance scheduling and recomputation against poplar logs and other leaves. The live leaf and fence paths
use current tags, as the worldgen helpers do. Decay and loot use the existing leaf/loot
paths; per-color decay drops and client ambient leaf particles have not been separately captured here.

Note blocks derive their instrument on placement and vertical neighbor updates from generated 26.3 instrument
data, including the four trumpet oxidation stages and waxed copper. The reference checks above-block instrument
priority, blocked playback, exact tunable pitch, sound IDs/volume/seeds, random draw order and block-event packets
that make vanilla clients produce note particles. Native lifecycle captures cover coalescing activations into the
block-event phase, using its final state and cancelling playback after replacement. Native server use-item cases
cover top-instrument head placement without tuning, side-face/ordinary-item tuning and correct skull rotation.
Door-hinge captures include mixed wood/iron/copper doors and distinguish full collision cubes from top slabs.
Custom player-head sound lookup from skull block entities
remains unsupported; a head without a custom sound is silent as in vanilla.

Block events now share one insertion-ordered queue, deduplicated by position, block type, event ID and parameter.
The phase runs after chunk/random ticks and before entities; it consumes events added while draining, validates
the block type at dispatch, and retries events in inactive chunks on a later tick. Block-event eligibility includes
vanilla's block-ticking fringe around players and ticking tickets, beyond entity ticking; loaded-only tickets do
not activate it. Event packets and note sounds send as the events dispatch, and the block changes an event makes go
out at the next tick's flush of block changes, as with vanilla's chunk broadcast (a piston's moving blocks aren't sent
at all). This order matches a vanilla capture, but hasn't been checked on a real client. Container lid
events carry the number of viewers of each physical block, including separate ender inventories and both double-
chest halves. Window lifecycle changes mark physical containers dirty, including direct opens, replacement,
disconnect, death and dimension changes; the level counts and enqueues on its simulation path. Lid event payloads
also re-read the current viewer count at dispatch, so coalescing earlier open/close requests cannot publish a stale
final count. Per-container
five-tick opener rechecks repair missed cleanup. Scheduled block ticks use the same block-ticking predicate while
retaining their complete-neighbor readiness checks. Notes, normal/sticky pistons,
chest/trapped/copper chest lids (both halves), ender chest and shulker lids, sulfur geysers, gateway cooldowns and
block-spawner resets use this phase. Native captures cover both note/piston orders, mixed duplicates, notification-
queued playback during draining, every key field, replacement and inactive-chunk retry, plus the client event
forms for lids, bells, gateways, spawners and geysers. A bell rung by a player's use queues its event here too;
bells rung by redstone or projectiles, and the bell's server-side ringing state, are still missing (see
[waxing, bells and redstone ore](waxing-bells-and-redstone-ore.md)).

Shelves of every wood, straw beds' sleep rules and golden dandelions' age locking are implemented and replayed
from their own captures (`VanillaShelves`, `VanillaStrawBeds`, `VanillaGoldenDandelions`; see
[scenario captures](../scenario-captures.md)). What still differs:

- **Shelves.** Breaking one shows the shared player break effects (its break sound instead of level event 2001, and
  `block_destroy` after the removal's effects), pinned in three cases ([player break effects](player-break-effects.md)).
  Whether taking an item into the hand is heard follows the item's default `use_effects` only; a stack whose own
  components change `interact_vibrations` is heard as its item would be. Any item access to a shelf, a plugin's
  included, emits the `block_activate` game events vanilla's hopper transfer does (three for an item put in, two for
  one taken out). `align_items_to_bottom` is kept as loaded; nothing on the server changes it, as in vanilla.
- **Straw beds and beds.** A player lying down falls the last eighth of a block onto a bed, which bounces in vanilla's
  own player physics; Obsidian leaves players' movement to their clients, and pins the bounce's game events in one
  case. A forced respawn point counts a block with a collision shape as solid, where vanilla reads its legacy solid
  flag. Respawn anchors still stand players up with Obsidian's own search rather than vanilla's
  `RespawnAnchorBlock.findStandUpPosition`. Villagers asleep in a bed aren't woken by a player using it. The players
  are told how many sleep as a player falls asleep or wakes; a player joining or leaving is announced on the next
  tick rather than at once.
- **Golden dandelions.** A dandelion is checked after the mob's own interaction, as vanilla's `AgeableMob` comes after
  its subclasses' food and buckets; a mob of another kind whose interaction takes a golden dandelion first would
  differ. Bees in a hive don't keep or honour their age lock while inside (vanilla's `BeehiveBlockEntity.Occupant`).
  Three cases pin mob behaviour outside the age lock: a horse eating plays no sound or `eat` event, a zombie horse
  doesn't anger at an interaction, and a baby villager neither refuses one with its sound nor stays despawnable.
- Poplar sapling growth is tracked separately below; poplar boats remain entity work.

Sulfur, chiseled/polished sulfur and sulfur bricks use the generated properties, sounds, shapes and loot. Their
stairs, slabs and walls share the existing placement and waterlogging APIs. Potent sulfur validates its water and
heat source, saves its countdown, times periodic eruptions with positional random, applies gas nausea and launches
entities. Block events start the vanilla client's geyser particles and sound loop; ambient bubbles and gas particles
are client behavior. Sulfur spikes support placement, shaping, delayed support loss, random growth, falling,
shattering and fast-trident breakage. `VanillaSulfurBlocks` replays `sulfur_scenarios.json` and `sulfur_fixtures.json`;
the latter is recorded by `SulfurFixtures.java` through `CaptureFixtures=sulfur`.

Remaining sulfur limits:

- Falling spikes (and falling pointed dripstone, which shares them) use the shared simplified falling-block flight
  and collision implementation. They always break where they land, where vanilla places one that lands in a cell it
  can hang in. Captures cover
  support-loss timing, shattering, raw impact damage and vertical sand contacts with full/partial floors and a
  moving piston. General horizontal flight and per-tick spike trajectories remain unaudited. Falling
  entities do not yet persist the spike's damage multiplier. Helmet-specific damage reduction and durability loss
  for `damages_helmet` damage sources are not implemented in the shared combat path.
- Tridents respect the projectile-breaking and mob-griefing game rules and reject adventure/spectator owners.
  Adventure-mode projectile `can_break` predicates are not evaluated yet.
- Gas uses the existing entity eye-height implementations; remaining species/pose eye-height differences belong
  to the entity behavior work.

**Where to start:** extend the sulfur, construction/music, shelf, straw bed or golden dandelion references before
changing the covered behavior. The sulfur cube and cushion entities are left out of the port; see [26.3 gameplay left out of the port](26-3-gameplay-left-out-of-the-port.md).
