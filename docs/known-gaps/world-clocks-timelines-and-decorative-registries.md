# World clocks, timelines and decorative registries


**Status:** Phase 4 data support implemented.

Game time and the overworld and End clocks are independent. Dimensions share the world's clocks; saves and full
time syncs retain each clock's ticks, fractional tick, rate and paused state. Sky light, monster burning, early-game
patrol tracks and sleep markers come from the generated timeline data. `/time of <clock>` selects a clock for
set, add, pause, resume, rate and query operations. Vanilla's command failure semantics and timeline-specific
queries are implemented, including timeline ticks/repetitions and failures for unchanged clock state.

Trim materials retain 26.3's `palette_id` and text description. Pottery patterns and their item components are read
from the generated registries, including stack overrides. Their values, clocks and timeline sampling are compared
with the real vanilla capture in `DataSystemsFixtures.java`.
