using Obsidian.API.World.Features;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>randomTick</c> overrides of plant and crop-like blocks (milestone 2). Each member is named for the Mojang
/// declaring class dumped in <c>block_random_tick_classes.json</c>, and each body follows that class's decompiled
/// <c>randomTick</c>.
/// </summary>
internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior NetherWartBlock { get; } = new NetherWart();

    internal static IRandomTickBehavior CocoaBlock { get; } = new Cocoa();

    internal static IRandomTickBehavior SugarCaneBlock { get; } = new SugarCane();

    internal static IRandomTickBehavior CactusBlock { get; } = new Cactus();

    internal static IRandomTickBehavior SweetBerryBushBlock { get; } = new SweetBerryBush();

    internal static IRandomTickBehavior BambooStalkBlock { get; } = new BambooStalk();

    internal static IRandomTickBehavior BambooSaplingBlock { get; } = new BambooSapling();

    internal static IRandomTickBehavior ChorusFlowerBlock { get; } = new ChorusFlower();

    internal static IRandomTickBehavior MushroomBlock { get; } = new Mushroom();

    internal static IRandomTickBehavior SpreadingSnowyDirtBlock { get; } = new SpreadingSnowyDirt();

    // NetherWartBlock.randomTick: below age 3, a draw of nextInt(10) that comes up 0 adds an age.
    private sealed class NetherWart : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var age = Growth.Age(block);
            if (age < 3 && random.NextInt(10) == 0)
                Growth.SetAndShow(level, position, block.WithProperty("age", age + 1));
        }
    }

    // CocoaBlock.randomTick: a draw of nextInt(5) that comes up 0 adds an age below age 2.
    private sealed class Cocoa : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (random.NextInt(5) != 0)
                return;

            var age = Growth.Age(block);
            if (age < 2)
                Growth.SetAndShow(level, position, block.WithProperty("age", age + 1));
        }
    }

    // SugarCaneBlock.randomTick: with air above, counting the cane below (the stalk's own block included); fewer than
    // three means the cane grows. At age 15 it plants a new cane above and resets to age 0.
    private sealed class SugarCane : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var above = position + Vector.Up;
            if (!Growth.BlockAt(level, above).IsAir)
                return;

            var height = 1;
            while (Growth.BlockAt(level, position + new Vector(0, -height, 0)).RegistryId == block.RegistryId)
                height++;

            if (height >= 3)
                return;

            var age = Growth.Age(block);
            if (age == 15)
            {
                Growth.SetAndShow(level, above, BlocksRegistry.GetDefault(BlockIds.SugarCane));
                Growth.SetAndShow(level, position, block.WithProperty("age", 0));
            }
            else
            {
                Growth.SetAndShow(level, position, block.WithProperty("age", age + 1));
            }
        }
    }

    // CactusBlock.randomTick: with air above, counting the cactus below (up to three). At age 8 it may flower; at age 15
    // below three it plants a cactus above and resets; below age 15 it ages.
    private sealed class Cactus : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var above = position + Vector.Up;
            if (!Growth.BlockAt(level, above).IsAir)
                return;

            var age = Growth.Age(block);
            var height = 1;
            while (Growth.BlockAt(level, position + new Vector(0, -height, 0)).RegistryId == block.RegistryId)
            {
                if (++height == 3 && age == 15)
                    return;
            }

            if (age == 8 && CanSurvive(level, above))
            {
                var chance = height >= 3 ? 0.25 : 0.1;
                if (random.NextDouble() <= chance)
                    Growth.SetAndShow(level, above, BlocksRegistry.GetDefault(BlockIds.CactusFlower));
            }
            else if (age == 15 && height < 3)
            {
                Growth.SetAndShow(level, above, BlocksRegistry.GetDefault(BlockIds.Cactus));
                Growth.SetAndShow(level, position, block.WithProperty("age", 0));
            }

            if (age < 15)
                Growth.SetAndShow(level, position, block.WithProperty("age", age + 1));
        }

        // Vanilla's CactusBlock.canSurvive: no solid or lava neighbour on the sides, sand or cactus below, and no liquid above.
        private static bool CanSurvive(AbstractLevel level, Vector above)
        {
            var position = above + Vector.Down;
            foreach (var side in new[] { new Vector(1, 0, 0), new Vector(-1, 0, 0), new Vector(0, 0, 1), new Vector(0, 0, -1) })
            {
                var neighbour = Growth.BlockAt(level, above + side);
                if (neighbour.IsSolid() || neighbour.GetFluid() is FluidKind.Lava or FluidKind.FlowingLava)
                    return false;
            }

            var below = Growth.BlockAt(level, position);
            var supported = below.RegistryId == BlockIds.Cactus || TagsRegistry.Block.Sand.Entries.Contains(below.RegistryId);
            return supported && Growth.BlockAt(level, above).GetFluid() == FluidKind.Empty;
        }
    }

    // SweetBerryBushBlock.randomTick: below age 3, a draw of nextInt(5) that comes up 0 with a raw brightness of 9 above
    // adds an age.
    private sealed class SweetBerryBush : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var age = Growth.Age(block);
            if (age < 3 && random.NextInt(5) == 0 && Growth.RawBrightness(level, position + Vector.Up, ambientDarkness: 0) >= 9)
                Growth.SetAndShow(level, position, block.WithProperty("age", age + 1));
        }
    }

    // BambooStalkBlock.randomTick: a stalk with stage 0 draws nextInt(3) that comes up 0, with air above and a raw
    // brightness of 9 there, then grows up to the bamboo's own height (below 16).
    private sealed class BambooStalk : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (block.GetProperty("stage") != "0")
                return;

            if (random.NextInt(3) != 0 || !Growth.BlockAt(level, position + Vector.Up).IsAir
                || Growth.RawBrightness(level, position + Vector.Up, ambientDarkness: 0) < 9)
            {
                return;
            }

            var height = CountBambooBelow(level, position, block) + 1;
            if (height < 16)
                Grow(level, position, block, random, height);
        }

        // Vanilla's BambooStalkBlock.getHeightBelowUpToMax: bamboo stacked below, up to 16.
        private static int CountBambooBelow(AbstractLevel level, Vector position, IBlock block)
        {
            var count = 0;
            while (count < 16 && Growth.BlockAt(level, position + new Vector(0, -(count + 1), 0)).RegistryId == block.RegistryId)
                count++;

            return count;
        }

        // Vanilla's BambooStalkBlock.growBamboo: the new stalk's leaves and stage, and the leaves of the stalks below.
        private static void Grow(AbstractLevel level, Vector position, IBlock block, IRandomSource random, int height)
        {
            var below = Growth.BlockAt(level, position + Vector.Down);
            var twoBelowPosition = position + new Vector(0, -2, 0);
            var twoBelow = Growth.BlockAt(level, twoBelowPosition);
            var leaves = "none";

            if (height >= 1)
            {
                if (below.RegistryId != BlockIds.Bamboo || below.GetProperty("leaves") == "none")
                {
                    leaves = "small";
                }
                else
                {
                    leaves = "large";
                    if (twoBelow.RegistryId == BlockIds.Bamboo)
                    {
                        Growth.SetAndShow(level, position + Vector.Down, below.WithProperty("leaves", "small"));
                        Growth.SetAndShow(level, twoBelowPosition, twoBelow.WithProperty("leaves", "none"));
                    }
                }
            }

            var age = block.GetProperty("age") != "1" && twoBelow.RegistryId != BlockIds.Bamboo ? 0 : 1;
            // Vanilla draws only for a stalk of 11 or more; the stage is 0 unless that draw is at least 0.25 (or the height is 15).
            var stageZero = (height < 11 || random.NextFloat() >= 0.25f) && height != 15;

            var bamboo = BlocksRegistry.GetDefault(BlockIds.Bamboo)
                .WithProperty("age", age)
                .WithProperty("leaves", leaves)
                .WithProperty("stage", stageZero ? 0 : 1);
            Growth.SetAndShow(level, position + Vector.Up, bamboo);
        }
    }

    // BambooSaplingBlock.randomTick: a draw of nextInt(3) that comes up 0, with air above and a raw brightness of 9 there,
    // turns the sapling's space above into a small bamboo stalk.
    private sealed class BambooSapling : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var above = position + Vector.Up;
            if (random.NextInt(3) == 0 && Growth.BlockAt(level, above).IsAir
                && Growth.RawBrightness(level, above, ambientDarkness: 0) >= 9)
            {
                Growth.SetAndShow(level, above, BlocksRegistry.GetDefault(BlockIds.Bamboo).WithProperty("leaves", "small"));
            }
        }
    }

    // ChorusFlowerBlock.randomTick: below age 5 with air above (inside the build height), a flower grows a new stalk above
    // when it stands on end stone, on a chorus stalk that is short or passes a draw, or on air. Otherwise it spreads to
    // up to four horizontal neighbours (one more above end stone) while below age 4, and dies to age 5 if none takes.
    // Sounds (levelEvent 1033/1034) are not played.
    private sealed class ChorusFlower : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            var above = position + Vector.Up;
            if (!Growth.BlockAt(level, above).IsAir || above.Y > level.MinY + level.Height - 1)
                return;

            var age = Growth.Age(block);
            if (age >= 5)
                return;

            var canGrow = false;
            var endStoneAtStalkTop = false;
            var below = Growth.BlockAt(level, position + Vector.Down);
            if (below.Is(BlockIds.EndStone))
            {
                canGrow = true;
            }
            else if (below.RegistryId == BlockIds.ChorusPlant)
            {
                var height = 1;
                for (var i = 0; i < 4; i++)
                {
                    var stalk = Growth.BlockAt(level, position + new Vector(0, -(height + 1), 0));
                    if (stalk.RegistryId != BlockIds.ChorusPlant)
                    {
                        endStoneAtStalkTop = stalk.Is(BlockIds.EndStone);
                        break;
                    }

                    height++;
                }

                if (height < 2 || height <= random.NextInt(endStoneAtStalkTop ? 5 : 4))
                    canGrow = true;
            }
            else if (below.IsAir)
            {
                canGrow = true;
            }

            if (canGrow && AllNeighborsEmpty(level, above, except: null)
                && Growth.BlockAt(level, position + new Vector(0, 2, 0)).IsAir)
            {
                var stalk = ChorusConnections(level, position);
                Growth.SetAndShow(level, position, stalk);
                Growth.SetAndShow(level, above, Flower(age));
            }
            else if (age < 4)
            {
                var attempts = random.NextInt(4) + (endStoneAtStalkTop ? 1 : 0);
                var grew = false;
                for (var i = 0; i < attempts; i++)
                {
                    var direction = FeatureHelpers.RandomHorizontal(random);
                    var target = position.Offset(direction);
                    if (Growth.BlockAt(level, target).IsAir && Growth.BlockAt(level, target + Vector.Down).IsAir
                        && AllNeighborsEmpty(level, target, except: direction.Opposite()))
                    {
                        Growth.SetAndShow(level, target, Flower(age + 1));
                        grew = true;
                    }
                }

                if (grew)
                    Growth.SetAndShow(level, position, ChorusConnections(level, position));
                else
                    Growth.SetAndShow(level, position, Flower(5));
            }
            else
            {
                Growth.SetAndShow(level, position, Flower(5));
            }
        }

        private static IBlock Flower(int age) => BlocksRegistry.ChorusFlower.WithProperty("age", age);

        private static IBlock ChorusConnections(AbstractLevel level, Vector position) =>
            ChorusPlantFeature.WithConnections(p => Growth.BlockAt(level, p), position);

        // Vanilla's ChorusFlowerBlock.allNeighborsEmpty: the four horizontal neighbours are air, except one face.
        private static bool AllNeighborsEmpty(AbstractLevel level, Vector position, BlockFace? except)
        {
            foreach (var face in FeatureHelpers.Horizontal)
            {
                if (face != except && !Growth.BlockAt(level, position.Offset(face)).IsAir)
                    return false;
            }

            return true;
        }
    }

    // MushroomBlock.randomTick: a draw of nextInt(25) that comes up 0 spreads the mushroom. Five of the same mushroom in
    // the 9x3x9 box around it stop the spread. Otherwise it tries four random spots (the origin moves to each spot that
    // passes survival), then places a copy on the last spot if that passes survival too.
    private sealed class Mushroom : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (random.NextInt(25) != 0)
                return;

            var count = 5;
            for (var x = -4; x <= 4; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    for (var z = -4; z <= 4; z++)
                    {
                        if (Growth.BlockAt(level, position + new Vector(x, y, z)).RegistryId == block.RegistryId && --count <= 0)
                            return;
                    }
                }
            }

            var origin = position;
            var target = Offset(origin, random);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (Growth.BlockAt(level, target).IsAir && CanSurvive(level, target, block))
                    origin = target;

                target = Offset(origin, random);
            }

            if (Growth.BlockAt(level, target).IsAir && CanSurvive(level, target, block))
                Growth.SetAndShow(level, target, block);
        }

        // Vanilla: x in -1..1, y as nextInt(2) - nextInt(2) (two draws), z in -1..1.
        private static Vector Offset(Vector origin, IRandomSource random)
        {
            var x = random.NextInt(3) - 1;
            var y = random.NextInt(2) - random.NextInt(2);
            var z = random.NextInt(3) - 1;
            return origin + new Vector(x, y, z);
        }

        // Vanilla MushroomBlock.canSurvive: a mushroom grow block below, or a solid render block below with a raw brightness under 13.
        private static readonly BlockSet MushroomGrowBlock = new("#minecraft:mushroom_grow_block");

        private static bool CanSurvive(AbstractLevel level, Vector position, IBlock block)
        {
            var below = Growth.BlockAt(level, position + Vector.Down);
            if (MushroomGrowBlock.Contains(below))
                return true;

            return Growth.RawBrightness(level, position, ambientDarkness: 0) < 13 && below.IsSolidRender();
        }
    }

    // SpreadingSnowyDirtBlock.randomTick, shared by grass and mycelium: a block that cannot stay grass turns to dirt;
    // otherwise, with at least 9 raw light above, four random spots within reach that are dirt and can take the grass
    // become this block (snowy when snow sits above them). Changes update neighbours as vanilla's setBlockAndUpdate does.
    private sealed class SpreadingSnowyDirt : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (!CanBeGrass(level, position, block))
            {
                Replace(level, position, BlocksRegistry.Dirt);
                return;
            }

            if (Growth.RawBrightness(level, position + Vector.Up, level.SkyDarken()) < 9)
                return;

            var spread = BlocksRegistry.GetDefault(block.RegistryId);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var target = position + new Vector(random.NextInt(3) - 1, random.NextInt(5) - 3, random.NextInt(3) - 1);
                var targetBlock = Growth.BlockAt(level, target);
                if (targetBlock.Is(BlockIds.Dirt) && CanPropagate(level, target, spread))
                {
                    var snowy = Growth.BlockAt(level, target + Vector.Up).Is(BlockIds.Snow);
                    Replace(level, target, spread.WithProperty("snowy", snowy));
                }
            }
        }

        private static void Replace(AbstractLevel level, Vector position, IBlock block)
        {
            Growth.SetAndShow(level, position, block);
            level.UpdateNeighbors(position, block);
        }

        // Vanilla SpreadingSnowyDirtBlock.canBeGrass: a snow layer of one above keeps it; a full water source above
        // stops it; otherwise the light that gets through the block above must be under 15.
        private static bool CanBeGrass(AbstractLevel level, Vector position, IBlock block)
        {
            var above = Growth.BlockAt(level, position + Vector.Up);
            if (above.Is(BlockIds.Snow) && above.GetProperty("layers") == "1")
                return true;

            if (above.FluidAmount() == 8)
                return false;

            var lightBlock = BlockLight.LightShapesOcclude(block, above, BlockFace.Up) ? 16 : above.LightBlock();
            return lightBlock < 15;
        }

        // Vanilla canPropagate: the spot can be grass, and no water lies above it.
        private static bool CanPropagate(AbstractLevel level, Vector position, IBlock spread) =>
            CanBeGrass(level, position, spread) && !FluidState.Of(Growth.BlockAt(level, position + Vector.Up)).IsWater;
    }
}
