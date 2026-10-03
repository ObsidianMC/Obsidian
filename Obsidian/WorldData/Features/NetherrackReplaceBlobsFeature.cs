namespace Obsidian.WorldData.Features;

/// <summary>
/// Replaces <see cref="Target"/> blocks in a rough blob below the origin (basalt and blackstone blobs), like vanilla's
/// ReplaceBlobsFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:netherrack_replace_blobs")]
public sealed class NetherrackReplaceBlobsFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:netherrack_replace_blobs";

    /// <summary>
    /// The block type that gets replaced (properties are ignored).
    /// </summary>
    public required SimpleBlockState Target { get; init; }

    public required SimpleBlockState State { get; init; }

    public required IIntProvider Radius { get; init; }

    private IBlock TargetBlock => field ??= BlocksRegistry.GetFromSimpleState(this.Target);

    private IBlock ReplaceBlock => field ??= BlocksRegistry.GetFromSimpleState(this.State);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        if (!level.EnsureCanWrite(context.Origin))
            return false;

        var start = context.Origin.AtY(Math.Clamp(context.Origin.Y, level.MinY + 1, level.MinY + level.Height - 1));
        var center = this.FindTarget(level, start);
        if (center is null)
            return false;

        var radiusX = this.Radius.Sample(random);
        var radiusY = this.Radius.Sample(random);
        var radiusZ = this.Radius.Sample(random);
        var maxDistance = Math.Max(radiusX, Math.Max(radiusY, radiusZ));
        var placed = false;

        foreach (var position in FeatureHelpers.WithinManhattan(center.Value, radiusX, radiusY, radiusZ))
        {
            if (FeatureHelpers.DistManhattan(position, center.Value) > maxDistance)
                break;

            if (level.GetBlock(position).RegistryId == this.TargetBlock.RegistryId)
            {
                level.SetBlock(position, this.ReplaceBlock);
                placed = true;
            }
        }

        return placed;
    }

    private Vector? FindTarget(IWorldGenLevel level, Vector position)
    {
        while (position.Y > level.MinY + 1)
        {
            if (level.GetBlock(position).RegistryId == this.TargetBlock.RegistryId)
                return position;

            position += Vector.Down;
        }

        return null;
    }
}
