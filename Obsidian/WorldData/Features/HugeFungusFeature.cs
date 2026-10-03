using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Crimson and warped huge fungi (stem, wart hat, shroomlights and weeping vines), like vanilla's HugeFungusFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:huge_fungus")]
public sealed class HugeFungusFeature : ConfiguredFeatureBase
{
    private const float HugeProbability = 0.06f;

    public override string Type => "minecraft:huge_fungus";

    /// <summary>
    /// The block the fungus must stand on (nylium).
    /// </summary>
    public required SimpleBlockState ValidBaseBlock { get; init; }

    public required SimpleBlockState StemState { get; init; }

    public required SimpleBlockState HatState { get; init; }

    public required SimpleBlockState DecorState { get; init; }

    /// <summary>
    /// Extra blocks the stem may replace (plants and saplings).
    /// </summary>
    public required IBlockPredicate ReplaceableBlocks { get; init; }

    /// <summary>
    /// Grown from a planted fungus: skips the height check and the huge variant, and clears blocks it builds through.
    /// </summary>
    public bool Planted { get; init; }

    private IBlock ValidBase => field ??= BlocksRegistry.GetFromSimpleState(this.ValidBaseBlock);

    private IBlock Stem => field ??= BlocksRegistry.GetFromSimpleState(this.StemState);

    private IBlock Hat => field ??= BlocksRegistry.GetFromSimpleState(this.HatState);

    private IBlock Decor => field ??= BlocksRegistry.GetFromSimpleState(this.DecorState);

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || level.GetBlock(origin + Vector.Down).RegistryId != this.ValidBase.RegistryId)
            return false;

        var height = FeatureHelpers.NextInt(random, 4, 13);
        if (random.NextInt(12) == 0)
            height *= 2;

        // Vanilla compares the absolute top Y with the generator's depth.
        if (!this.Planted && origin.Y + height + 1 >= context.Generation.Height)
            return false;

        var huge = !this.Planted && random.NextFloat() < HugeProbability;
        level.SetBlock(origin, Air);
        this.PlaceStem(level, random, origin, height, huge);
        this.PlaceHat(level, random, origin, height, huge);
        return true;
    }

    private bool IsReplaceable(IWorldGenLevel level, Vector position, bool stem) =>
        level.GetBlock(position).CanBeReplaced() || stem && this.ReplaceableBlocks.Test(level, position);

    private void PlaceStem(IWorldGenLevel level, IRandomSource random, Vector origin, int height, bool huge)
    {
        var radius = huge ? 1 : 0;

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                var corner = huge && Math.Abs(dx) == radius && Math.Abs(dz) == radius;

                for (var y = 0; y < height; y++)
                {
                    var position = origin + new Vector(dx, y, dz);
                    if (!this.IsReplaceable(level, position, true))
                        continue;

                    if (this.Planted)
                    {
                        // Vanilla destroys whatever stands here first (drops aren't modeled); the stem replaces it anyway.
                        if (!level.GetBlock(position + Vector.Down).IsAir)
                            level.SetBlock(position, Air);

                        level.SetBlock(position, this.Stem);
                    }
                    else if (corner)
                    {
                        if (random.NextFloat() < 0.1f)
                            level.SetBlock(position, this.Stem);
                    }
                    else
                    {
                        level.SetBlock(position, this.Stem);
                    }
                }
            }
        }
    }

    private void PlaceHat(IWorldGenLevel level, IRandomSource random, Vector origin, int height, bool huge)
    {
        var netherWart = this.Hat.Material == Material.NetherWartBlock;
        var hatHeight = Math.Min(random.NextInt(1 + height / 3) + 5, height);
        var hatStart = height - hatHeight;

        for (var y = hatStart; y <= height; y++)
        {
            var radius = y < height - random.NextInt(3) ? 2 : 1;
            if (hatHeight > 8 && y < hatStart + 4)
                radius = 3;

            if (huge)
                radius++;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var edgeX = dx == -radius || dx == radius;
                    var edgeZ = dz == -radius || dz == radius;
                    var inside = !edgeX && !edgeZ && y != height;
                    var corner = edgeX && edgeZ;
                    var lowRing = y < hatStart + 3;
                    var position = origin + new Vector(dx, y, dz);

                    if (!this.IsReplaceable(level, position, false))
                        continue;

                    if (this.Planted && !level.GetBlock(position + Vector.Down).IsAir)
                        level.SetBlock(position, Air);

                    if (lowRing)
                    {
                        if (!inside)
                            this.PlaceHatDropBlock(level, random, position, netherWart);
                    }
                    else if (inside)
                    {
                        this.PlaceHatBlock(level, random, position, 0.1f, 0.2f, netherWart ? 0.1f : 0.0f);
                    }
                    else if (corner)
                    {
                        this.PlaceHatBlock(level, random, position, 0.01f, 0.7f, netherWart ? 0.083f : 0.0f);
                    }
                    else
                    {
                        this.PlaceHatBlock(level, random, position, 5.0E-4f, 0.98f, netherWart ? 0.07f : 0.0f);
                    }
                }
            }
        }
    }

    private void PlaceHatBlock(IWorldGenLevel level, IRandomSource random, Vector position, float decorChance, float hatChance,
        float vineChance)
    {
        if (random.NextFloat() < decorChance)
        {
            level.SetBlock(position, this.Decor);
        }
        else if (random.NextFloat() < hatChance)
        {
            level.SetBlock(position, this.Hat);
            if (random.NextFloat() < vineChance)
                TryPlaceWeepingVines(level, random, position);
        }
    }

    private void PlaceHatDropBlock(IWorldGenLevel level, IRandomSource random, Vector position, bool netherWart)
    {
        if (level.GetBlock(position + Vector.Down).RegistryId == this.Hat.RegistryId)
        {
            level.SetBlock(position, this.Hat);
        }
        else if (random.NextFloat() < 0.15)
        {
            level.SetBlock(position, this.Hat);
            if (netherWart && random.NextInt(11) == 0)
                TryPlaceWeepingVines(level, random, position);
        }
    }

    private static void TryPlaceWeepingVines(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        var below = position + Vector.Down;
        if (!level.GetBlock(below).IsAir)
            return;

        var length = FeatureHelpers.NextInt(random, 1, 5);
        if (random.NextInt(7) == 0)
            length *= 2;

        WeepingVinesFeature.PlaceWeepingVinesColumn(level, random, below, length, 23, 25);
    }
}
