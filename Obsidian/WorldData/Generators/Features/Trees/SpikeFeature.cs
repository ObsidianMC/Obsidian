using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// 26.3's <c>minecraft:spike</c>, the ice spike generalised: a tapering spike of one state mirrored below its base, and
/// roots of it reaching down to Y 50, on a block <c>can_place_on</c> accepts, replacing air and <c>can_replace</c>.
/// </summary>
internal sealed class SpikeFeature(FeatureRegistry registry, JsonElement json)
{
    private readonly IBlock state = FeatureRegistry.ReadBlock(json.GetProperty("state"));
    private readonly BlockPredicate canPlaceOn = registry.Predicate(json.GetProperty("can_place_on"));
    private readonly BlockPredicate canReplace = registry.Predicate(json.GetProperty("can_replace"));

    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var level = context.Level;

        while (level.GetBlock(origin).IsAir && origin.Y > level.MinY + 2)
        {
            origin += Vector.Down;
        }

        if (!this.canPlaceOn(level, origin))
        {
            return false;
        }

        origin += (0, random.NextInt(4), 0);

        var height = random.NextInt(4) + 7;
        var width = height / 4 + random.NextInt(2);

        if (width > 1 && random.NextInt(60) == 0)
        {
            origin += (0, 10 + random.NextInt(30), 0);
        }

        for (var y = 0; y < height; y++)
        {
            this.PlaceLayer(level, random, origin, y, height, width);
        }

        var rootRadius = Math.Clamp(width - 1, 0, 1);

        for (var dx = -rootRadius; dx <= rootRadius; dx++)
        {
            for (var dz = -rootRadius; dz <= rootRadius; dz++)
            {
                this.PlaceRoot(level, random, origin + (dx, -1, dz), dx, dz);
            }
        }

        return true;
    }

    // One layer of the spike and its mirror below the base; rim blocks draw a quarter chance of being left out.
    private void PlaceLayer(IWorldGenLevel level, IRandomSource random, Vector origin, int y, int height, int width)
    {
        var radius = (1.0f - (float)y / height) * width;
        var extent = (int)MathF.Ceiling(radius);

        for (var dx = -extent; dx <= extent; dx++)
        {
            var fx = Math.Abs(dx) - 0.25f;

            for (var dz = -extent; dz <= extent; dz++)
            {
                var fz = Math.Abs(dz) - 0.25f;
                var center = dx == 0 && dz == 0;
                var inside = center || !(fx * fx + fz * fz > radius * radius);
                var onRim = dx == -extent || dx == extent || dz == -extent || dz == extent;

                if (!inside || (onRim && random.NextFloat() > 0.75f))
                {
                    continue;
                }

                this.TryReplace(level, origin + (dx, y, dz));

                if (y != 0 && extent > 1)
                {
                    this.TryReplace(level, origin + (dx, -y, dz));
                }
            }
        }
    }

    private void TryReplace(IWorldGenLevel level, Vector position)
    {
        if (level.GetBlock(position).IsAir || this.canReplace(level, position))
        {
            level.SetBlock(position, this.state);
        }
    }

    // Columns down to Y 50, the corners shorter, skipping a few blocks each time a run ends.
    private void PlaceRoot(IWorldGenLevel level, IRandomSource random, Vector position, int dx, int dz)
    {
        var corner = Math.Abs(dx) == 1 && Math.Abs(dz) == 1;
        var remaining = corner ? random.NextInt(5) : 50;

        while (position.Y > 50)
        {
            var existing = level.GetBlock(position);
            var replaceable = existing.IsAir || this.canReplace(level, position) || existing.IsSameState(this.state);

            if (!replaceable)
            {
                return;
            }

            level.SetBlock(position, this.state);
            position += Vector.Down;
            remaining--;

            if (remaining <= 0)
            {
                position += (0, -(random.NextInt(5) + 1), 0);
                remaining = random.NextInt(5);
            }
        }
    }
}
