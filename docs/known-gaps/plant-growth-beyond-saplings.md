# Plant growth beyond saplings


**Status:** open.

Random ticks grow saplings and the mangrove propagule, and bone meal grows saplings, azaleas, crops, grass blocks and
short grass and ferns (see [plugin content](../plugin-content.md#saplings)). Crops don't grow on random ticks, and bone
meal does nothing to vanilla's other plants (stems, berries, mushrooms, moss, nylium, cocoa, water plants and the
like).

Random ticks sample the same sections and draw the same positions as vanilla's, but only saplings and the propagule
do anything with them: leaves don't decay, ice and snow don't melt, copper doesn't weather, and so on. Lava's random
tick draws its chances in vanilla's order but sets nothing on fire, since Obsidian has no fire spread or flammability.
Vanilla's chunk ticks also draw for rain and snow (`tickPrecipitation`, one time in 48 per random tick), which
Obsidian neither draws nor does, so the level random and the position sequence part from vanilla's there.

**Where to start:** each plant is another case of `Growth` (its random tick, bone meal target, chance and growth),
placing features through `LiveFeatureLevel`; crops' random ticks need vanilla's `CropBlock.getGrowthSpeed`. Other
blocks' random ticks go next to the plants' in `AbstractLevel.TickChunkBlocks`, and lava's fire in
`AbstractLevel.RandomTickLava` once blocks know vanilla's `ignitedByLava`.
