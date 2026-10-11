namespace Obsidian.WorldData.Features;

/// <summary>
/// Grows a blue ice cluster under packed ice below sea level, like vanilla's BlueIceFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:blue_ice")]
public sealed class BlueIceFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:blue_ice";

    private static IBlock BlueIce => field ??= BlocksRegistry.BlueIce;

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || origin.Y > level.SeaLevel - 1)
            return false;

        if (!level.GetBlock(origin).Is(BlockIds.Water) && !level.GetBlock(origin + Vector.Down).Is(BlockIds.Water))
            return false;

        var touchesPackedIce = false;
        foreach (var face in FeatureHelpers.Directions)
        {
            if (face != BlockFace.Down && level.GetBlock(origin.Offset(face)).RegistryId == BlockIds.PackedIce)
            {
                touchesPackedIce = true;
                break;
            }
        }

        if (!touchesPackedIce)
            return false;

        level.SetBlock(origin, BlueIce);

        for (var i = 0; i < 200; i++)
        {
            var dy = random.NextInt(5) - random.NextInt(6);
            var spread = 3;
            if (dy < 2)
                spread += dy / 2;

            if (spread < 1)
                continue;

            var position = origin + new Vector(random.NextInt(spread) - random.NextInt(spread), dy,
                random.NextInt(spread) - random.NextInt(spread));
            var existing = level.GetBlock(position);
            if (!existing.IsAir && existing.RegistryId is not (BlockIds.Water or BlockIds.PackedIce or BlockIds.Ice))
                continue;

            foreach (var face in FeatureHelpers.Directions)
            {
                if (level.GetBlock(position.Offset(face)).RegistryId == BlockIds.BlueIce)
                {
                    level.SetBlock(position, BlueIce);
                    break;
                }
            }
        }

        return true;
    }
}
