using Obsidian.API.Blocks;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>randomTick</c> overrides of plants that grow into the blocks around them: kelp and the cave, weeping and
/// twisting vines (<c>GrowingPlantHeadBlock</c>), melon and pumpkin stems (<c>StemBlock</c>), wall vines
/// (<c>VineBlock</c>) and the pitcher crop (<c>PitcherCropBlock</c>). Checked against the
/// <c>growing_plant_ticks</c> capture.
/// </summary>
/// <remarks>
/// Writes go through the level's block updates, so neighbours get vanilla's shape updates: that is how a growing
/// plant's old head becomes its body (see <see cref="KelpBlockBehavior"/>). Every draw comes from the random the tick
/// passes, which is the level's, as vanilla's <c>getRandom()</c> is during a random tick.
/// </remarks>
internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior GrowingPlantHeadBlock { get; } = new GrowingPlantHead();

    internal static IRandomTickBehavior StemBlock { get; } = new Stem();

    internal static IRandomTickBehavior VineBlock { get; } = new Vine();

    internal static IRandomTickBehavior PitcherCropBlock { get; } = new PitcherCrop();

    /// <summary>
    /// A plant that grows a block at a time from its head: the head's age goes up by one in the new head, and the old
    /// head becomes the plant's body through its shape update. Heads stop at age 25.
    /// </summary>
    private sealed class GrowingPlantHead : IRandomTickBehavior
    {
        private const int MaxAge = 25;

        // CaveVinesBlock: the chance that a new head carries glow berries.
        private const float BerryChance = 0.11f;

        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (Kind(block.RegistryId) is not (BlockFace direction, double chance))
                return;

            var age = Growth.Age(block);

            // Vanilla's GrowingPlantHeadBlock.randomTick: one nextDouble against the plant's chance, which decides
            // whether the head grows, before the block it grows into is read.
            if (age >= MaxAge || random.NextDouble() >= chance)
                return;

            var blocks = level.BlockUpdates;
            var target = position.Offset(direction);
            if (!CanGrowInto(block.RegistryId, blocks.GetBlock(target)))
                return;

            var grown = block.WithProperty("age", age + 1);

            // CaveVinesBlock.getGrowIntoState: a nextFloat after the growth draw rolls the new head's berries.
            if (block.RegistryId == BlockIds.CaveVines)
                grown = grown.WithProperty("berries", random.NextFloat() < BerryChance);

            blocks.SetBlock(target, grown, BlockUpdateFlags.All);
        }

        // Each head's growth direction and its chance to grow on a random tick (vanilla's growPerTickProbability).
        private static (BlockFace Direction, double Chance)? Kind(int head) => head switch
        {
            BlockIds.Kelp => (BlockFace.Up, 0.14),
            BlockIds.CaveVines => (BlockFace.Down, 0.1),
            BlockIds.WeepingVines => (BlockFace.Down, 0.1),
            BlockIds.TwistingVines => (BlockFace.Up, 0.1),
            _ => null
        };

        // Kelp grows into water, flowing or not; the vines into air.
        private static bool CanGrowInto(int head, IBlock target) => head == BlockIds.Kelp
            ? target.RegistryId == BlockIds.Water
            : target.IsAir;
    }

    /// <summary>
    /// A melon or pumpkin stem: in light of at least 9 it ages like a crop until age 7, and then tries to set its
    /// fruit beside it, on a block that supports the fruit, turning into the attached stem facing it.
    /// </summary>
    private sealed class Stem : IRandomTickBehavior
    {
        private const int MaxAge = 7;

        private const int MinimumLight = 9;

        private static readonly WorldgenTags.BlockGroup melonSupport = WorldgenTags.Blocks("supports_melon_stem_fruit");

        private static readonly WorldgenTags.BlockGroup pumpkinSupport =
            WorldgenTags.Blocks("supports_pumpkin_stem_fruit");

        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (Growth.RawBrightness(level, position, ambientDarkness: 0) < MinimumLight)
                return;

            // Vanilla's StemBlock.randomTick: the crop's draw of nextInt(floor(25 / speed) + 1), then at the last
            // age a horizontal direction (nextInt(4) in north, east, south, west order) for the fruit.
            var speed = CropGrowth.GrowthSpeed(level, position, block);
            if (random.NextInt((int)(25f / speed) + 1) != 0)
                return;

            var blocks = level.BlockUpdates;
            var age = Growth.Age(block);
            if (age < MaxAge)
            {
                blocks.SetBlock(position, block.WithProperty("age", age + 1), BlockUpdateFlags.Clients);
                return;
            }

            var direction = FeatureHelpers.RandomHorizontal(random);
            var target = position.Offset(direction);
            var melon = block.RegistryId == BlockIds.MelonStem;
            var support = melon ? melonSupport : pumpkinSupport;
            if (!blocks.GetBlock(target).IsAir || !support.Contains(blocks.GetBlock(target + Vector.Down)))
                return;

            var fruit = BlocksRegistry.GetDefault(melon ? BlockIds.Melon : BlockIds.Pumpkin);
            var attached = BlocksRegistry.GetDefault(melon ? BlockIds.AttachedMelonStem : BlockIds.AttachedPumpkinStem)
                .WithProperty("facing", FeatureHelpers.FaceName(direction));

            blocks.SetBlock(target, fruit, BlockUpdateFlags.All);
            blocks.SetBlock(position, attached, BlockUpdateFlags.All);
        }
    }

    /// <summary>
    /// A wall vine: while the <c>spread_vines</c> rule holds, one random tick in four picks a direction and spreads
    /// the vine along a wall, around a corner, onto a ceiling, up, or down onto the vine or air below it. Spreading
    /// sideways and up stop once five vines stand within four blocks across and one up or down.
    /// </summary>
    private sealed class Vine : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (!level.LevelData.GetBooleanRule("spread_vines"))
                return;

            // Vanilla's VineBlock.randomTick draws nextInt(4), then a direction of nextInt(6) in down, up, north,
            // south, west, east order; what follows draws only on the branch it takes.
            if (random.NextInt(4) != 0)
                return;

            var direction = FeatureHelpers.RandomDirection(random);
            var blocks = level.BlockUpdates;

            // A side the vine already covers falls through to growing up or down, like a vertical direction.
            if (!direction.IsVertical() && !HasFace(block, direction))
            {
                SpreadSideways(blocks, position, block, direction, random);
                return;
            }

            if (direction == BlockFace.Up && position.Y < level.MinY + level.Height - 1)
            {
                if (CanSupportAtFace(blocks, position, BlockFace.Up))
                {
                    blocks.SetBlock(position, block.WithProperty("up", true), BlockUpdateFlags.Clients);
                    return;
                }

                if (blocks.GetBlock(position + Vector.Up).IsAir)
                {
                    Climb(blocks, position, block, random);
                    return;
                }
            }

            if (position.Y > level.MinY)
                GrowDown(blocks, position, block, random);
        }

        private static void SpreadSideways(IBlockLevel level, Vector position, IBlock vine, BlockFace direction,
            IRandomSource random)
        {
            if (!CanSpread(level, position))
                return;

            var target = position.Offset(direction);
            if (!level.GetBlock(target).IsAir)
            {
                // A solid side: the vine covers it.
                if (IsAcceptableNeighbour(level, target, direction))
                {
                    var covered = vine.WithProperty(FeatureHelpers.FaceName(direction), true);
                    level.SetBlock(position, covered, BlockUpdateFlags.Clients);
                }

                return;
            }

            var clockwise = direction.ClockWise();
            var counterClockwise = direction.CounterClockWise();
            var hasClockwise = HasFace(vine, clockwise);
            var hasCounterClockwise = HasFace(vine, counterClockwise);
            var clockwiseTarget = target.Offset(clockwise);
            var counterClockwiseTarget = target.Offset(counterClockwise);

            // Along the wall the vine covers, onto the block beside the one it hangs on.
            if (hasClockwise && IsAcceptableNeighbour(level, clockwiseTarget, clockwise))
            {
                level.SetBlock(target, VineWith(clockwise), BlockUpdateFlags.Clients);
                return;
            }

            if (hasCounterClockwise && IsAcceptableNeighbour(level, counterClockwiseTarget, counterClockwise))
            {
                level.SetBlock(target, VineWith(counterClockwise), BlockUpdateFlags.Clients);
                return;
            }

            // Around the corner of the block it hangs on.
            var back = direction.Opposite();
            if (hasClockwise
                && level.GetBlock(clockwiseTarget).IsAir
                && IsAcceptableNeighbour(level, position.Offset(clockwise), back))
            {
                level.SetBlock(clockwiseTarget, VineWith(back), BlockUpdateFlags.Clients);
                return;
            }

            if (hasCounterClockwise
                && level.GetBlock(counterClockwiseTarget).IsAir
                && IsAcceptableNeighbour(level, position.Offset(counterClockwise), back))
            {
                level.SetBlock(counterClockwiseTarget, VineWith(back), BlockUpdateFlags.Clients);
                return;
            }

            // Vanilla compares the nextFloat with the double 0.05, which a float 0.05 isn't.
            if ((double)random.NextFloat() < 0.05 && IsAcceptableNeighbour(level, target + Vector.Up, BlockFace.Up))
                level.SetBlock(target, VineWith(BlockFace.Up), BlockUpdateFlags.Clients);
        }

        // Into the air above: each side the vine covers stays on a nextBoolean of false where a wall holds it there.
        // The draw comes first, so all four are drawn.
        private static void Climb(IBlockLevel level, Vector position, IBlock vine, IRandomSource random)
        {
            if (!CanSpread(level, position))
                return;

            var above = position + Vector.Up;
            var grown = vine;
            foreach (var face in FeatureHelpers.Horizontal)
            {
                if (random.NextBoolean() || !IsAcceptableNeighbour(level, above.Offset(face), face))
                    grown = grown.WithProperty(FeatureHelpers.FaceName(face), false);
            }

            if (HasSide(grown))
                level.SetBlock(above, grown, BlockUpdateFlags.Clients);
        }

        // Onto air or a vine below, which takes each side the vine covers on a nextBoolean of true. The spread limit
        // doesn't apply here.
        private static void GrowDown(IBlockLevel level, Vector position, IBlock vine, IRandomSource random)
        {
            var belowPosition = position + Vector.Down;
            var below = level.GetBlock(belowPosition);
            if (!below.IsAir && below.RegistryId != BlockIds.Vine)
                return;

            var before = below.IsAir ? BlocksRegistry.GetDefault(BlockIds.Vine) : below;
            var after = before;
            foreach (var face in FeatureHelpers.Horizontal)
            {
                if (random.NextBoolean() && HasFace(vine, face))
                    after = after.WithProperty(FeatureHelpers.FaceName(face), true);
            }

            if (!after.IsSameState(before) && HasSide(after))
                level.SetBlock(belowPosition, after, BlockUpdateFlags.Clients);
        }

        // Vanilla's VineBlock.canSpread: at most four vines, this one included, in the box four blocks across and one
        // up or down around it.
        private static bool CanSpread(IBlockLevel level, Vector position)
        {
            var vines = 0;
            for (var y = -1; y <= 1; y++)
            {
                for (var z = -4; z <= 4; z++)
                {
                    for (var x = -4; x <= 4; x++)
                    {
                        if (level.GetBlock(position + new Vector(x, y, z)).RegistryId == BlockIds.Vine && ++vines > 4)
                            return false;
                    }
                }
            }

            return true;
        }

        // Vanilla's VineBlock.canSupportAtFace: a full face on that side, or for a side, the vine above covering it.
        private static bool CanSupportAtFace(IBlockLevel level, Vector position, BlockFace face)
        {
            if (face == BlockFace.Down)
                return false;

            if (IsAcceptableNeighbour(level, position.Offset(face), face))
                return true;

            if (face.IsVertical())
                return false;

            var above = level.GetBlock(position + Vector.Up);
            return above.RegistryId == BlockIds.Vine && HasFace(above, face);
        }

        private static bool IsAcceptableNeighbour(IBlockLevel level, Vector neighbour, BlockFace direction) =>
            BlockSurvival.CanAttachTo(level.GetBlock(neighbour), direction);

        private static bool HasFace(IBlock vine, BlockFace face) =>
            vine.GetProperty(FeatureHelpers.FaceName(face)) == "true";

        private static bool HasSide(IBlock vine)
        {
            foreach (var face in FeatureHelpers.Horizontal)
            {
                if (HasFace(vine, face))
                    return true;
            }

            return false;
        }

        private static IBlock VineWith(BlockFace face) =>
            BlocksRegistry.GetDefault(BlockIds.Vine).WithProperty(FeatureHelpers.FaceName(face), true);
    }

    /// <summary>
    /// The pitcher crop's lower half: it draws like a crop first, then grows one age in light of at least 8, growing
    /// its upper half from age 3 when the block above is air or pitcher crop. The upper half and age 4 don't tick.
    /// </summary>
    private sealed class PitcherCrop : IRandomTickBehavior
    {
        private const int MaxAge = 4;

        // Vanilla's PitcherCropBlock: the age from which the crop has an upper half.
        private const int DoubleAge = 3;

        private const int MinimumLight = 8;

        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            // Vanilla's PitcherCropBlock.randomTick draws nextInt(floor(25 / speed) + 1) before reading the light.
            var speed = CropGrowth.GrowthSpeed(level, position, block);
            if (random.NextInt((int)(25f / speed) + 1) == 0)
                Grow(level, position, block);
        }

        // Vanilla shares this growth with the pitcher crop's bone meal, which Obsidian doesn't grow it with yet.
        private static void Grow(AbstractLevel level, Vector lower, IBlock block)
        {
            var age = Growth.Age(block);
            var grown = Math.Min(age + 1, MaxAge);
            var upper = lower + Vector.Up;
            var blocks = level.BlockUpdates;

            if (age >= MaxAge
                || Growth.RawBrightness(level, lower, ambientDarkness: 0) < MinimumLight
                || level.IsOutsideBuildHeight(upper.Y))
            {
                return;
            }

            if (grown >= DoubleAge && !CanGrowInto(blocks.GetBlock(upper)))
                return;

            var lowerState = block.WithProperty("age", grown);
            blocks.SetBlock(lower, lowerState, BlockUpdateFlags.Clients);

            if (grown >= DoubleAge)
                blocks.SetBlock(upper, lowerState.WithProperty("half", "upper"), BlockUpdateFlags.All);
        }

        private static bool CanGrowInto(IBlock block) => block.IsAir || block.RegistryId == BlockIds.PitcherCrop;
    }
}
