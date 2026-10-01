namespace Obsidian.WorldData.Features;

/// <summary>
/// Flat lava pools with a magma rim set into the floor (basalt deltas), like vanilla's DeltaFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:delta_feature")]
public sealed class DeltaFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:delta_feature";

    public required SimpleBlockState Contents { get; init; }

    public required SimpleBlockState Rim { get; init; }

    public required IIntProvider Size { get; init; }

    public required IIntProvider RimSize { get; init; }

    private IBlock ContentsBlock => field ??= BlocksRegistry.GetFromSimpleState(this.Contents);

    private IBlock RimBlock => field ??= BlocksRegistry.GetFromSimpleState(this.Rim);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var placed = false;
        var hasRim = random.NextDouble() < 0.9;
        var rimX = hasRim ? this.RimSize.Sample(random) : 0;
        var rimZ = hasRim ? this.RimSize.Sample(random) : 0;
        var placeRim = hasRim && rimX != 0 && rimZ != 0;
        var sizeX = this.Size.Sample(random);
        var sizeZ = this.Size.Sample(random);
        var maxDistance = Math.Max(sizeX, sizeZ);

        foreach (var position in FeatureHelpers.WithinManhattan(origin, sizeX, 0, sizeZ))
        {
            if (FeatureHelpers.DistManhattan(position, origin) > maxDistance)
                break;

            if (!this.IsClear(level, position))
                continue;

            if (placeRim)
            {
                placed = true;
                level.SetBlock(position, this.RimBlock);
            }

            var pool = position + new Vector(rimX, 0, rimZ);
            if (this.IsClear(level, pool))
            {
                placed = true;
                level.SetBlock(pool, this.ContentsBlock);
            }
        }

        return placed;
    }

    // Solid on the five lower sides, open above, and not already contents or a protected block.
    private bool IsClear(IWorldGenLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        if (state.RegistryId == this.ContentsBlock.RegistryId || CannotReplace(state))
            return false;

        foreach (var face in FeatureHelpers.Directions)
        {
            var air = level.GetBlock(position.Offset(face)).IsAir;
            if (air != (face == BlockFace.Up))
                return false;
        }

        return true;
    }

    private static bool CannotReplace(IBlock block) => block.Material is Material.Bedrock or Material.NetherBricks
        or Material.NetherBrickFence or Material.NetherBrickStairs or Material.NetherWart or Material.Chest or Material.Spawner;
}
