# Plant growth beyond saplings


**Status:** open.

Random ticks grow saplings and the mangrove propagule, crops, nether wart, cocoa, sugar cane, cactus, sweet berries,
bamboo, chorus flowers and mushrooms, and spread grass and mycelium (`RandomTickBehaviors`). Kelp, the cave, weeping
and twisting vines (cave vines rolling glow berries), melon and pumpkin stems (setting their fruit), wall vines and the
pitcher crop grow too, and are checked against vanilla's draws and results (`growing_plant_ticks.json`, see
[scenario captures](../scenario-captures.md)). The poplar sapling doesn't grow (see
[saplings 26.3 added](saplings-that-26-3-added.md)).

Bone meal grows saplings, azaleas, crops, grass blocks and short grass and ferns (see
[plugin content](../plugin-content.md#saplings)). It does nothing to vanilla's other plants (stems, the pitcher crop,
berries, mushrooms, moss, nylium, cocoa, kelp, the cave and nether vines and the like). In vanilla, bone meal that
brings a stem to age 7 runs the stem's random tick, and the pitcher crop's bone meal is its random-tick growth; both
are there to share in `GrowingPlantRandomTicks.cs`.

Random ticks sample the same sections and draw the same positions as vanilla's. Copper now weathers through all
three age transitions, including connected doors and chests, with a captured neighborhood scan and random draws. A
block whose vanilla random tick Obsidian doesn't have draws nothing: ice and snow still don't melt, and so on. Lava's
random tick draws its chances in vanilla's order and sets fire to the blocks vanilla's lava ignites; fire then ticks,
spreads and burns out with vanilla's draws (see `fire_ticks.json` in [scenario captures](../scenario-captures.md)).
Vanilla's chunk ticks also draw for rain and snow (`tickPrecipitation`, one time in 48 per random tick), which
Obsidian neither draws nor does, so the level random and the position sequence part from vanilla's there.

Around the plants that grow:

- **Shape updates run twice.** A head growing into a gap below its plant is stored with every flag, and kelp gets
  a water tick vanilla doesn't schedule, in six captured cases; the cause and the cases are in
  [vanilla blocks' reactions to block updates](vanilla-blocks-reactions-to-block-updates.md).
- **Pitcher crops have no shape handler.** A lower half that loses its farmland or light, or a half whose other half
  the world removes, stays; vanilla's `PitcherCropBlock.updateShape` breaks it.
- **Cave, weeping and twisting vines don't break when they lose their support.** Their shape update schedules the
  support check as vanilla's does, but the check (`KelpBlockBehavior.Tick`) only breaks kelp.
- **Crops' growth speed finds farmland by id.** `CropGrowth.GrowthSpeed` counts farmland where vanilla reads the
  `grows_crops` tag; 26.3's tag holds only farmland, so a data pack adding to it isn't followed.

**Where to start:** a plant's random tick is a member of `RandomTickBehaviors` named for the vanilla class that
declares it, which the block generator binds (a class without one reports `OBSGEN001`). Capture it first by adding
cases to `growing_plant_ticks.json` (`CaptureFixtures=growing_plant_ticks`). Bone meal goes in `Growth`, and kelp's and
the vines' shape updates in `GrowingPlantShapes`.
