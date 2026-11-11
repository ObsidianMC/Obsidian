# Plant growth beyond saplings


**Status:** open.

Random ticks grow saplings and the mangrove propagule, crops, nether wart, cocoa, sugar cane, cactus, sweet berries,
bamboo, chorus flowers and mushrooms, and spread grass and mycelium (`RandomTickBehaviors`). Kelp, the cave, weeping
and twisting vines (cave vines rolling glow berries), melon and pumpkin stems (setting their fruit), wall vines and the
pitcher crop grow too, and are checked against vanilla's draws and results (`growing_plant_ticks.json`, see
[scenario captures](../scenario-captures.md)). Saplings, the poplar's included, pick their trees from 26.3's weighted
lists, with vanilla's draws (checked by the same capture).

Bone meal grows saplings, azaleas, crops, grass blocks and short grass and ferns (see
[plugin content](../plugin-content.md#saplings)), and stems, the pitcher crop, sweet berries, cocoa, kelp, the cave,
weeping and twisting vines, rooted dirt, mangrove leaves, netherrack, nylium, fungi, mushrooms and moss
(`Growth.Plants.cs`), all checked against vanilla in `growing_plant_ticks.json`. The capture's features are stand-ins
that record where they're placed, so the features these grow into are checked by world generation's own replays, not
here; and since no stand-in is a tree, a sapling's room for its shortest tree's trunk is only checked where the trunk
fits either way.

Bone meal still does nothing to vanilla's other bonemealable blocks: bamboo and its shoot, glow lichen, sea pickles,
seagrass, small and big dripleaf, flower beds (pink petals, wildflowers, leaf litter), tall flowers, hanging moss,
moss carpets, short and tall dry grass, firefly bushes, bushes and shelf mushrooms; nor to water (vanilla's
`growWaterPlant`). Each is a `BonemealPlant` in `Growth.Plants.cs`, captured first as a `bonemeal` case.

Random ticks sample the same sections and draw the same positions as vanilla's. Copper now weathers through all
three age transitions, including connected doors and chests, with a captured neighborhood scan and random draws.
Ice, frosted ice and snow layers melt, and the chunk tick draws for rain and snow as vanilla's does
(`tickPrecipitation`, one time in 48 per random tick): snow settles and builds up, water freezes and cauldrons fill,
replayed from vanilla in `melting_weather.json`. Lava's random tick draws its chances in vanilla's order and sets
fire to the blocks vanilla's lava ignites; fire then ticks, spreads and burns out with vanilla's draws (see
`fire_ticks.json` in [scenario captures](../scenario-captures.md)).

Weather still switches immediately. Vanilla's precipitation pass tests rain strength above 0.2 while weather fades
in and out, so transition ticks can differ. The level's growth/precipitation random is not shared with mob spawning
and thunder as in vanilla: only the draw order inside one chunk's tick matches.

Around the plants that grow:

- **Pitcher crops have no shape handler.** A lower half that loses its farmland or light, or a half whose other half
  the world removes, stays; vanilla's `PitcherCropBlock.updateShape` breaks it.
- **Cave, weeping and twisting vines don't break when they lose their support.** Their shape update schedules the
  support check as vanilla's does, but the check (`KelpBlockBehavior.Tick`) only breaks kelp.
- **Crops' growth speed finds farmland by id.** `CropGrowth.GrowthSpeed` counts farmland where vanilla reads the
  `grows_crops` tag; 26.3's tag holds only farmland, so a data pack adding to it isn't followed.

**Where to start:** a plant's random tick is a member of `RandomTickBehaviors` named for the vanilla class that
declares it, which the block generator binds (a class without one reports `OBSGEN001`). Capture it first by adding
cases to `growing_plant_ticks.json` (`CaptureFixtures=growing_plant_ticks`). Bone meal goes in `Growth` (a
`BonemealPlant` per vanilla class in `Growth.Plants.cs`), and kelp's and the vines' shape updates in
`GrowingPlantShapes`.
