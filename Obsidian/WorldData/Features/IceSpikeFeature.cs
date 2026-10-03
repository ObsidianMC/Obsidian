namespace Obsidian.WorldData.Features;

/// <summary>
/// Packed ice spikes standing on snow, some very tall, with a packed ice root below, like vanilla's IceSpikeFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:ice_spike")]
public sealed class IceSpikeFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:ice_spike";

    private static IBlock PackedIce => field ??= BlocksRegistry.Get(Material.PackedIce);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        while (level.GetBlock(origin).IsAir && origin.Y > level.MinY + 2)
            origin += Vector.Down;

        if (level.GetBlock(origin).Material != Material.SnowBlock)
            return false;

        origin += new Vector(0, random.NextInt(4), 0);
        var height = random.NextInt(4) + 7;
        var width = height / 4 + random.NextInt(2);
        if (width > 1 && random.NextInt(60) == 0)
            origin += new Vector(0, 10 + random.NextInt(30), 0);

        for (var y = 0; y < height; y++)
        {
            var radius = (1.0f - (float)y / height) * width;
            var extent = FeatureHelpers.Ceil(radius);

            for (var dx = -extent; dx <= extent; dx++)
            {
                var fx = Math.Abs(dx) - 0.25f;

                for (var dz = -extent; dz <= extent; dz++)
                {
                    var fz = Math.Abs(dz) - 0.25f;
                    var inside = dx == 0 && dz == 0 || !(fx * fx + fz * fz > radius * radius);
                    var onRim = dx == -extent || dx == extent || dz == -extent || dz == extent;

                    // Rim columns are dropped with a 25% roll (only rolled for rim positions inside the radius).
                    if (!inside || onRim && random.NextFloat() > 0.75f)
                        continue;

                    var above = origin + new Vector(dx, y, dz);
                    if (CanReplace(level.GetBlock(above)))
                        level.SetBlock(above, PackedIce);

                    if (y != 0 && extent > 1)
                    {
                        var mirrored = origin + new Vector(dx, -y, dz);
                        if (CanReplace(level.GetBlock(mirrored)))
                            level.SetBlock(mirrored, PackedIce);
                    }
                }
            }
        }

        var rootRadius = Math.Clamp(width - 1, 0, 1);
        for (var dx = -rootRadius; dx <= rootRadius; dx++)
        {
            for (var dz = -rootRadius; dz <= rootRadius; dz++)
            {
                var position = origin + new Vector(dx, -1, dz);
                var remaining = 50;
                if (Math.Abs(dx) == 1 && Math.Abs(dz) == 1)
                    remaining = random.NextInt(5);

                while (position.Y > 50)
                {
                    var existing = level.GetBlock(position);
                    if (!existing.IsAir && !FeatureHelpers.IsDirt(existing) && existing.Material is not (Material.SnowBlock or Material.Ice or Material.PackedIce))
                        break;

                    level.SetBlock(position, PackedIce);
                    position += Vector.Down;

                    if (--remaining <= 0)
                    {
                        position += new Vector(0, -(random.NextInt(5) + 1), 0);
                        remaining = random.NextInt(5);
                    }
                }
            }
        }

        return true;
    }

    private static bool CanReplace(IBlock block) =>
        block.IsAir || FeatureHelpers.IsDirt(block) || block.Material is Material.SnowBlock or Material.Ice;
}
