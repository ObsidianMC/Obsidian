using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// A vanilla class's <c>randomTick</c> override, as Obsidian runs it on a random tick of a block it owns.
/// </summary>
internal interface IRandomTickBehavior
{
    /// <summary>Runs the random tick, drawing from <paramref name="random"/> in vanilla's order.</summary>
    void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random);
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>Block.randomTick</c>. Member names match the Mojang
/// declaring classes dumped as <c>block_random_tick_classes.json</c>; the block generator binds blocks directly to
/// them (<c>BlocksRegistry.GetRandomTickBehavior</c>). A block whose owner has no member keeps the existing path in
/// <see cref="Growth"/>.
/// </summary>
internal static partial class RandomTickBehaviors
{
    private static readonly IRandomTickBehavior Crop = new CropGrowth();

    // One member per Mojang class that owns an implemented randomTick override. CropBlock's wheat, carrots and potatoes
    // grow; beetroots and torchflowers draw first and then run CropBlock's (see CropAfterThirdsDraw).
    internal static IRandomTickBehavior CropBlock => Crop;

    internal static IRandomTickBehavior FarmlandBlock { get; } = new FarmlandBlockBehavior();

    internal static IRandomTickBehavior LeavesBlock { get; } = new LeavesBlockBehavior();

    // Separate instances, so that Growth can tell the sapling's owner from the propagule's by reference.
    internal static IRandomTickBehavior SaplingBlock { get; } = new PlantGrowth();

    internal static IRandomTickBehavior MangrovePropaguleBlock { get; } = new PlantGrowth();

    /// <summary>
    /// Vanilla's <c>SaplingBlock.randomTick</c> and <c>MangrovePropaguleBlock.randomTick</c>: the growth
    /// <see cref="Growth.RandomTick"/> draws and places, for the tree the owner's <c>treeGrower</c> names.
    /// </summary>
    private sealed class PlantGrowth : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
            Growth.RandomTick(level, position, block, random);
    }

    internal static IRandomTickBehavior BeetrootBlock { get; } = new CropAfterThirdsDraw(Crop);

    internal static IRandomTickBehavior TorchflowerCropBlock { get; } = new CropAfterThirdsDraw(Crop);

    /// <summary>
    /// Vanilla's <c>BeetrootBlock.randomTick</c> and <c>TorchflowerCropBlock.randomTick</c>: a draw of
    /// <c>nextInt(3)</c>, and the crop's random tick (the <c>CropBlock</c> one) unless that draw is 0.
    /// </summary>
    private sealed class CropAfterThirdsDraw(IRandomTickBehavior crop) : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (random.NextInt(3) != 0)
                crop.RandomTick(level, position, block, random);
        }
    }

    /// <summary>
    /// Vanilla's <c>CropBlock.randomTick</c>: in light of at least 9 at the crop, a crop below its last age grows one
    /// age when a draw of <c>nextInt(floor(25 / speed) + 1)</c> comes up 0, where the speed is
    /// <c>getGrowthSpeed</c>. A crop in too little light, or at its last age, draws nothing.
    /// </summary>
    private sealed partial class CropGrowth : IRandomTickBehavior
    {
        // Vanilla's CropBlock.randomTick: the light a crop needs, read at the crop with no ambient darkness.
        private const int MinimumLight = 9;

        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (Growth.RawBrightness(level, position, ambientDarkness: 0) < MinimumLight)
                return;

            var age = Growth.Age(block);
            if (age >= Growth.MaxAge(block))
                return;

            var speed = GrowthSpeed(level, position, block);
            if (random.NextInt((int)(25f / speed) + 1) != 0)
                return;

            Growth.SetAndShow(level, position, Growth.GrownState(block, age + 1));
            Log.Grew(level.Logger, position, block.UnlocalizedName, age, age + 1);
        }

        // Vanilla's CropBlock.getGrowthSpeed: 1, plus for each of the nine farmland blocks below the crop (the crop's
        // own square and its neighbours, a quarter of the neighbours' worth) a point for dry farmland or three for
        // moist farmland. Then halved when crops stand both east and west and north and south of it, or else on any
        // diagonal.
        private static float GrowthSpeed(AbstractLevel level, Vector position, IBlock crop)
        {
            var below = position + Vector.Down;
            var speed = 1f;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var contribution = Farmland(Growth.BlockAt(level, below + new Vector(dx, 0, dz)));
                    if (dx != 0 || dz != 0)
                        contribution /= 4f;

                    speed += contribution;
                }
            }

            var acrossX = IsCrop(level, position + new Vector(-1, 0, 0), crop) || IsCrop(level, position + new Vector(1, 0, 0), crop);
            var acrossZ = IsCrop(level, position + new Vector(0, 0, -1), crop) || IsCrop(level, position + new Vector(0, 0, 1), crop);

            if (acrossX && acrossZ)
            {
                speed /= 2f;
            }
            else if (IsCrop(level, position + new Vector(-1, 0, -1), crop) || IsCrop(level, position + new Vector(1, 0, -1), crop)
                || IsCrop(level, position + new Vector(-1, 0, 1), crop) || IsCrop(level, position + new Vector(1, 0, 1), crop))
            {
                speed /= 2f;
            }

            return speed;
        }

        // Vanilla's CropBlock.getGrowthSpeed's farmland term: the farmland's moisture (vanilla's FarmlandBlock.MOISTURE) is
        // above 0 for three, and 0 for one.
        private static float Farmland(IBlock block)
        {
            if (!block.Is(BlockIds.Farmland))
                return 0f;

            return int.TryParse(block.GetProperty("moisture"), out var moisture) && moisture > 0 ? 3f : 1f;
        }

        private static bool IsCrop(AbstractLevel level, Vector position, IBlock crop) =>
            Growth.BlockAt(level, position).RegistryId == crop.RegistryId;

        private static partial class Log
        {
            [LoggerMessage(Level = LogLevel.Debug, Message = "Crop {Block} at {Position} grew from age {From} to {To}.")]
            public static partial void Grew(ILogger logger, Vector position, string block, int from, int to);
        }
    }
}
