using System.Collections.Frozen;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Bone meal on the plants that grow without a tree grower, each like the vanilla <c>BonemealableBlock</c> class
/// that owns it: stems, the pitcher crop, sweet berries, cocoa, kelp and the cave, weeping and twisting vines (heads
/// and bodies), rooted dirt, mangrove leaves, netherrack, nylium, fungi, mushrooms and moss. Checked against the
/// <c>growing_plant_ticks</c> capture.
/// </summary>
/// <remarks>
/// Writes go through the level's block updates with vanilla's flags; features go through
/// <see cref="Growth.PlaceFeature"/>. Bone meal's draws, where a plant has any, are vanilla's, in its order.
/// </remarks>
internal static partial class Growth
{
    // Vanilla's huge mushrooms need room for this much stem above them, and their cap's radius
    // (AbstractHugeMushroomFeature.MIN_MUSHROOM_HEIGHT).
    private const int MushroomStem = 4;

    private static readonly BonemealPlant stem = new Stem();

    private static readonly BonemealPlant pitcherCrop = new PitcherCrop();

    private static readonly BonemealPlant growingPlantHead = new GrowingPlantHead();

    private static readonly BonemealPlant growingPlantBody = new GrowingPlantBody();

    private static readonly BonemealPlant caveVines = new CaveVines();

    private static readonly FrozenDictionary<int, BonemealPlant> plants = new Dictionary<int, BonemealPlant>
    {
        [BlockIds.MelonStem] = stem,
        [BlockIds.PumpkinStem] = stem,
        [BlockIds.PitcherCrop] = pitcherCrop,
        [BlockIds.SweetBerryBush] = new AgingPlant(lastAge: 3),
        [BlockIds.Cocoa] = new AgingPlant(lastAge: 2),
        [BlockIds.Kelp] = growingPlantHead,
        [BlockIds.WeepingVines] = growingPlantHead,
        [BlockIds.TwistingVines] = growingPlantHead,
        [BlockIds.KelpPlant] = growingPlantBody,
        [BlockIds.WeepingVinesPlant] = growingPlantBody,
        [BlockIds.TwistingVinesPlant] = growingPlantBody,
        [BlockIds.CaveVines] = caveVines,
        [BlockIds.CaveVinesPlant] = caveVines,
        [BlockIds.RootedDirt] = new RootedDirt(),
        [BlockIds.MangroveLeaves] = new MangroveLeaves(),
        [BlockIds.Netherrack] = new Netherrack(),
        [BlockIds.CrimsonNylium] = new Nylium(),
        [BlockIds.WarpedNylium] = new Nylium(),
        [BlockIds.CrimsonFungus] = new NetherFungus("minecraft:crimson_fungus_planted", BlockIds.CrimsonNylium),
        [BlockIds.WarpedFungus] = new NetherFungus("minecraft:warped_fungus_planted", BlockIds.WarpedNylium),
        [BlockIds.BrownMushroom] = new Mushroom("minecraft:huge_brown_mushroom"),
        [BlockIds.RedMushroom] = new Mushroom("minecraft:huge_red_mushroom"),
        [BlockIds.MossBlock] = new FeaturePlacer("minecraft:moss_patch_bonemeal"),
        [BlockIds.PaleMossBlock] = new FeaturePlacer("minecraft:pale_moss_patch_bonemeal")
    }.ToFrozenDictionary();

    // The vanilla block's plant, if bone meal grows it here; plugin blocks have none.
    private static BonemealPlant? PlantFor(IBlock block)
    {
        if (BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out _))
            return null;

        return plants.GetValueOrDefault(block.RegistryId);
    }

    /// <summary>
    /// A vanilla <c>BonemealableBlock</c>: whether bone meal takes, whether it grows the plant this time, and how.
    /// </summary>
    private abstract class BonemealPlant
    {
        public abstract bool IsValidTarget(AbstractLevel level, Vector position, IBlock block);

        public virtual bool IsSuccess(IRandomSource random) => true;

        public abstract void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random);
    }

    /// <summary>
    /// <c>StemBlock</c>: two to five ages, up to the last, where the stem's random tick runs at once and may set its
    /// fruit.
    /// </summary>
    private sealed class Stem : BonemealPlant
    {
        private const int LastAge = 7;

        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            Age(block) != LastAge;

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            // Vanilla's Mth.nextInt(random, 2, 5).
            var age = Math.Min(LastAge, Age(block) + random.NextInt(4) + 2);
            var grown = block.WithProperty("age", age);
            level.BlockUpdates.SetBlock(position, grown, BlockUpdateFlags.Clients);

            if (age == LastAge)
                RandomTickBehaviors.StemBlock.RandomTick(level, position, grown, random);
        }
    }

    /// <summary>
    /// <c>PitcherCropBlock</c>: either half grows the crop an age, as its random tick does.
    /// </summary>
    private sealed class PitcherCrop : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            LowerHalf(level, position, block) is (Vector lower, IBlock lowerBlock)
            && RandomTickBehaviors.CanGrowPitcherCrop(level, lower, lowerBlock, Age(lowerBlock) + 1);

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (LowerHalf(level, position, block) is (Vector lower, IBlock lowerBlock))
                RandomTickBehaviors.GrowPitcherCrop(level, lower, lowerBlock);
        }

        // Vanilla's getLowerHalf: the block itself if it's the lower half, else the lower half below it.
        private static (Vector, IBlock)? LowerHalf(AbstractLevel level, Vector position, IBlock block)
        {
            if (IsLower(block))
                return (position, block);

            var below = position + Vector.Down;
            var lower = level.BlockUpdates.GetBlock(below);
            return IsLower(lower) ? (below, lower) : null;
        }

        private static bool IsLower(IBlock block) =>
            block.RegistryId == BlockIds.PitcherCrop && block.GetProperty("half") == "lower";
    }

    /// <summary>
    /// <c>SweetBerryBushBlock</c> and <c>CocoaBlock</c>: one age, up to the last.
    /// </summary>
    private sealed class AgingPlant(int lastAge) : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            Age(block) < lastAge;

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
            level.BlockUpdates.SetBlock(position, block.WithProperty("age", Age(block) + 1), BlockUpdateFlags.Clients);
    }

    /// <summary>
    /// <c>GrowingPlantHeadBlock</c>: kelp and the weeping and twisting vines grow new heads ahead of them, one for
    /// kelp and as many as the nether vines' chances allow, each an age older, while there's room.
    /// </summary>
    private sealed class GrowingPlantHead : BonemealPlant
    {
        private const int MaxAge = 25;

        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block)
        {
            var direction = RandomTickBehaviors.GrowthDirection(block.RegistryId);
            var target = position.Offset(direction);
            return RandomTickBehaviors.CanGrowInto(block.RegistryId, level.BlockUpdates.GetBlock(target))
                && !level.IsOutsideBuildHeight(target.Y);
        }

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var direction = RandomTickBehaviors.GrowthDirection(block.RegistryId);
            var target = position.Offset(direction);
            var age = Math.Min(Age(block) + 1, MaxAge);
            var count = BlocksToGrow(block.RegistryId, random);
            var blocks = level.BlockUpdates;

            for (var grown = 0; grown < count; grown++)
            {
                if (!RandomTickBehaviors.CanGrowInto(block.RegistryId, blocks.GetBlock(target))
                    || level.IsOutsideBuildHeight(target.Y))
                {
                    break;
                }

                blocks.SetBlock(target, block.WithProperty("age", age), BlockUpdateFlags.All);
                target = target.Offset(direction);
                age = Math.Min(age + 1, MaxAge);
            }
        }

        // Vanilla's getBlocksToGrowWhenBonemealed: kelp grows one; the nether vines draw nextDouble until it isn't
        // below a chance that starts at 1 and shrinks by 0.826 with each block (NetherVines).
        private static int BlocksToGrow(int head, IRandomSource random)
        {
            if (head == BlockIds.Kelp)
                return 1;

            var chance = 1.0;
            var count = 0;
            while (random.NextDouble() < chance)
            {
                chance *= 0.826;
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// <c>GrowingPlantBodyBlock</c>: the plant's head, found along its growth direction past its body, takes the bone
    /// meal.
    /// </summary>
    private sealed class GrowingPlantBody : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            Head(level, position, block) is Vector head
            && growingPlantHead.IsValidTarget(level, head, level.BlockUpdates.GetBlock(head));

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (Head(level, position, block) is Vector head)
                growingPlantHead.Grow(level, head, level.BlockUpdates.GetBlock(head), random);
        }

        // Vanilla's BlockUtil.getTopConnectedBlock: past the body blocks along the growth direction, the head.
        private static Vector? Head(AbstractLevel level, Vector position, IBlock body)
        {
            var headId = HeadOf(body.RegistryId);
            var direction = RandomTickBehaviors.GrowthDirection(headId);
            var ahead = position;
            IBlock block;
            do
            {
                ahead = ahead.Offset(direction);
                block = level.BlockUpdates.GetBlock(ahead);
            }
            while (block.RegistryId == body.RegistryId);

            return block.RegistryId == headId ? ahead : null;
        }

        private static int HeadOf(int body) => body switch
        {
            BlockIds.KelpPlant => BlockIds.Kelp,
            BlockIds.WeepingVinesPlant => BlockIds.WeepingVines,
            _ => BlockIds.TwistingVines
        };
    }

    /// <summary>
    /// <c>CaveVinesBlock</c> and <c>CaveVinesPlantBlock</c>: the vine grows glow berries where it has none.
    /// </summary>
    private sealed class CaveVines : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            block.GetProperty("berries") != "true";

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
            level.BlockUpdates.SetBlock(position, block.WithProperty("berries", true), BlockUpdateFlags.Clients);
    }

    /// <summary>
    /// <c>RootedDirtBlock</c>: hanging roots grow into the air below.
    /// </summary>
    private sealed class RootedDirt : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block)
        {
            var below = position + Vector.Down;
            return level.BlockUpdates.GetBlock(below).IsAir && !level.IsOutsideBuildHeight(below.Y);
        }

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
            level.BlockUpdates.SetBlock(position + Vector.Down, BlocksRegistry.GetDefault(BlockIds.HangingRoots),
                BlockUpdateFlags.All);
    }

    /// <summary>
    /// <c>MangroveLeavesBlock</c>: a new hanging propagule grows into the air below.
    /// </summary>
    private sealed class MangroveLeaves : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            level.BlockUpdates.GetBlock(position + Vector.Down).IsAir;

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            // Vanilla's MangrovePropaguleBlock.createNewHangingPropagule: hanging, at age 0.
            var propagule = BlocksRegistry.GetDefault(BlockIds.MangrovePropagule)
                .WithProperty("hanging", true)
                .WithProperty("age", 0);
            level.BlockUpdates.SetBlock(position + Vector.Down, propagule, BlockUpdateFlags.Clients);
        }
    }

    /// <summary>
    /// <c>NetherrackBlock</c>: netherrack under open sky light turns into the nylium around it, either kind when
    /// both are there.
    /// </summary>
    private sealed class Netherrack : BonemealPlant
    {
        // Vanilla's propagatesSkylightDown, which is what a light block of 0 means (see BlockLight.LightBlock).
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            level.BlockUpdates.GetBlock(position + Vector.Up).LightBlock() == 0
            && NyliumAround(level, position) != (false, false);

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var (warped, crimson) = NyliumAround(level, position);
            int nylium;
            if (warped && crimson)
                nylium = random.NextBoolean() ? BlockIds.WarpedNylium : BlockIds.CrimsonNylium;
            else if (warped)
                nylium = BlockIds.WarpedNylium;
            else if (crimson)
                nylium = BlockIds.CrimsonNylium;
            else
                return;

            level.BlockUpdates.SetBlock(position, BlocksRegistry.GetDefault(nylium), BlockUpdateFlags.All);
        }

        // Which nylium the #nylium blocks in the 3x3x3 box around the netherrack include.
        private static (bool Warped, bool Crimson) NyliumAround(AbstractLevel level, Vector position)
        {
            var warped = false;
            var crimson = false;
            for (var y = -1; y <= 1; y++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    for (var x = -1; x <= 1; x++)
                    {
                        var id = level.BlockUpdates.GetBlock(position + new Vector(x, y, z)).RegistryId;
                        if (!TagsRegistry.Block.Nylium.Entries.Contains(id))
                            continue;

                        warped |= id == BlockIds.WarpedNylium;
                        crimson |= id == BlockIds.CrimsonNylium;
                    }
                }
            }

            return (warped, crimson);
        }
    }

    /// <summary>
    /// <c>NyliumBlock</c>: either nylium grows the nether's bone meal vegetation above it (one feature for both since
    /// 26.1).
    /// </summary>
    private sealed class Nylium : BonemealPlant
    {
        private const string Feature = "minecraft:nylium_bonemeal";

        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block)
        {
            var above = position + Vector.Up;
            return level.BlockUpdates.GetBlock(above).IsAir && !level.IsOutsideBuildHeight(above.Y);
        }

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var above = position + Vector.Up;
            if (!level.IsOutsideBuildHeight(above.Y))
                PlaceFeature(level, Feature, above, random);
        }
    }

    /// <summary>
    /// <c>NetherFungusBlock</c>: a fungus on its own nylium grows into a huge fungus four times in ten.
    /// </summary>
    private sealed class NetherFungus(string feature, int nylium) : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            level.BlockUpdates.GetBlock(position + Vector.Down).RegistryId == nylium
            && !level.IsOutsideBuildHeight(position.Y + 1);

        public override bool IsSuccess(IRandomSource random) => random.NextFloat() < 0.4;

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
            PlaceFeature(level, feature, position, random);
    }

    /// <summary>
    /// <c>MushroomBlock</c>: a mushroom with room for its huge mushroom grows into it four times in ten, and comes
    /// back when it can't.
    /// </summary>
    private sealed class Mushroom(string feature) : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            level.GrowthFeatures.Find(feature) is not null
            && level.GrowthFeatures.MushroomFoliageRadius(feature) is int radius
            && !level.IsOutsideBuildHeight(position.Y + MushroomStem + radius);

        public override bool IsSuccess(IRandomSource random) => random.NextFloat() < 0.4;

        // Vanilla's growMushroom: the mushroom is removed (removeBlock, which leaves air: a mushroom holds no fluid),
        // the feature placed, and the mushroom set back if it wasn't.
        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (level.GrowthFeatures.Find(feature) is null)
                return;

            level.BlockUpdates.SetBlock(position, BlocksRegistry.Air, BlockUpdateFlags.All);

            if (!PlaceFeature(level, feature, position, random))
                level.BlockUpdates.SetBlock(position, block, BlockUpdateFlags.All);
        }
    }

    /// <summary>
    /// <c>BonemealableFeaturePlacerBlock</c>: moss and pale moss grow their patch above them.
    /// </summary>
    private sealed class FeaturePlacer(string feature) : BonemealPlant
    {
        public override bool IsValidTarget(AbstractLevel level, Vector position, IBlock block) =>
            level.BlockUpdates.GetBlock(position + Vector.Up).IsAir;

        public override void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
            PlaceFeature(level, feature, position + Vector.Up, random);
    }
}
