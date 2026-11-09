# Plant growth beyond saplings


**Status:** open.

Random ticks grow saplings and the mangrove propagule, and bone meal grows saplings, azaleas, crops, grass blocks and
short grass and ferns (see [plugin content](../plugin-content.md#saplings)). Crops don't grow on random ticks, and bone
meal does nothing to vanilla's other plants (stems, berries, mushrooms, moss, nylium, cocoa, water plants and the
like).

Random ticks sample the same sections and draw the same positions as vanilla's. Copper now weathers through all
three age transitions, including connected doors and chests, with a captured neighborhood scan and random draws.
Ice, frosted ice and snow layers melt, and the chunk tick draws for rain and snow as vanilla's does
(`tickPrecipitation`, one time in 48 per random tick): snow settles and builds up, water freezes and cauldrons fill,
replayed from vanilla in `melting_weather.json`. Lava's random tick draws its chances in vanilla's order and sets
fire to the blocks vanilla's lava ignites; fire then ticks, spreads and burns out with vanilla's draws (see
`fire_ticks.json` in [scenario captures](../scenario-captures.md)).

**Where to start:** each plant is another case of `Growth` (its random tick, bone meal target, chance and growth),
placing features through `LiveFeatureLevel`; crops' random ticks need vanilla's `CropBlock.getGrowthSpeed`. Other
blocks' random ticks go next to the plants' in `AbstractLevel.TickChunkBlocks`.
