using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// 26.3's huge red and brown mushrooms: a stem four to six blocks tall (doubled one time in twelve) under a cap, on a
/// block <c>can_place_on</c> accepts, where the space around stem and cap is air or leaves. Mushroom blocks replace
/// only air and <c>#replaceable_by_mushrooms</c>.
/// </summary>
internal abstract class HugeMushroomFeature
{
    private readonly StateProvider stemProvider;
    private readonly BlockPredicate canPlaceOn;
    private readonly HashSet<int> leaves;
    private readonly HashSet<int> replaceableByMushrooms;

    protected HugeMushroomFeature(FeatureRegistry registry, JsonElement json)
    {
        this.CapProvider = registry.Provider(json.GetProperty("cap_provider"));
        this.stemProvider = registry.Provider(json.GetProperty("stem_provider"));
        this.canPlaceOn = registry.Predicate(json.GetProperty("can_place_on"));
        this.FoliageRadius = json.TryGetProperty("foliage_radius", out var radius) ? radius.GetInt32() : 2;
        this.leaves = registry.BlockTag("minecraft:leaves");
        this.replaceableByMushrooms = registry.BlockTag("minecraft:replaceable_by_mushrooms");
    }

    protected StateProvider CapProvider { get; }

    protected int FoliageRadius { get; }

    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var level = context.Level;
        var height = random.NextInt(3) + 4;

        if (random.NextInt(12) == 0)
        {
            height *= 2;
        }

        if (!this.IsValidPosition(level, origin, height))
        {
            return false;
        }

        this.MakeCap(level, random, origin, height);

        for (var i = 0; i < height; i++)
        {
            var stem = FeatureRegistry.State(this.stemProvider, level, random, origin);
            this.PlaceMushroomBlock(level, origin + (0, i, 0), stem);
        }

        return true;
    }

    protected abstract int RadiusForHeight(int height, int y);

    protected abstract void MakeCap(IWorldGenLevel level, IRandomSource random, Vector origin, int height);

    protected void PlaceMushroomBlock(IWorldGenLevel level, Vector position, IBlock state)
    {
        var existing = level.GetBlock(position);

        if (existing.IsAir || this.replaceableByMushrooms.Contains(existing.RegistryId))
        {
            level.SetBlock(position, state);
        }
    }

    // Sets a cap state's sides where it has them all (a mushroom block, not a provider's other block).
    protected static IBlock WithSides(IBlock state, bool west, bool east, bool north, bool south)
    {
        if (!HasSides(state))
        {
            return state;
        }

        return state
            .WithProperty("west", west)
            .WithProperty("east", east)
            .WithProperty("north", north)
            .WithProperty("south", south);
    }

    protected static bool HasSides(IBlock state)
    {
        string[] sides = ["west", "east", "north", "south"];

        return sides.All(state.HasProperty);
    }

    private bool IsValidPosition(IWorldGenLevel level, Vector origin, int height)
    {
        var maxY = level.MinY + level.Height - 1;

        if (origin.Y < level.MinY + 1 || origin.Y + height + 1 > maxY)
        {
            return false;
        }

        if (!this.canPlaceOn(level, origin + Vector.Down))
        {
            return false;
        }

        for (var y = 0; y <= height; y++)
        {
            var radius = this.RadiusForHeight(height, y);

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var state = level.GetBlock(origin + (dx, y, dz));

                    if (!state.IsAir && !this.leaves.Contains(state.RegistryId))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}

/// <summary>A dome: three walled rows under a roof one block narrower.</summary>
internal sealed class HugeRedMushroomFeature(FeatureRegistry registry, JsonElement json)
    : HugeMushroomFeature(registry, json)
{
    protected override int RadiusForHeight(int height, int y)
    {
        var inCap = y < height && y >= height - 3;

        return inCap || y == height ? this.FoliageRadius : 0;
    }

    protected override void MakeCap(IWorldGenLevel level, IRandomSource random, Vector origin, int height)
    {
        var inner = this.FoliageRadius - 2;

        for (var y = height - 3; y <= height; y++)
        {
            var radius = y < height ? this.FoliageRadius : this.FoliageRadius - 1;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var edgeX = dx == -radius || dx == radius;
                    var edgeZ = dz == -radius || dz == radius;

                    // The walls keep only their sides: no corners and no inside.
                    if (y < height && edgeX == edgeZ)
                    {
                        continue;
                    }

                    var state = FeatureRegistry.State(this.CapProvider, level, random, origin);

                    if (HasSides(state) && state.HasProperty("up"))
                    {
                        state = WithSides(state, dx < -inner, dx > inner, dz < -inner, dz > inner);
                        state = state.WithProperty("up", y >= height - 1);
                    }

                    this.PlaceMushroomBlock(level, origin + (dx, y, dz), state);
                }
            }
        }
    }
}

/// <summary>A flat cap with cut corners on top of the stem.</summary>
internal sealed class HugeBrownMushroomFeature(FeatureRegistry registry, JsonElement json)
    : HugeMushroomFeature(registry, json)
{
    protected override int RadiusForHeight(int height, int y) => y <= 3 ? 0 : this.FoliageRadius;

    protected override void MakeCap(IWorldGenLevel level, IRandomSource random, Vector origin, int height)
    {
        var radius = this.FoliageRadius;

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                var minX = dx == -radius;
                var maxX = dx == radius;
                var minZ = dz == -radius;
                var maxZ = dz == radius;
                var edgeX = minX || maxX;
                var edgeZ = minZ || maxZ;

                if (edgeX && edgeZ)
                {
                    continue;
                }

                var west = minX || (edgeZ && dx == 1 - radius);
                var east = maxX || (edgeZ && dx == radius - 1);
                var north = minZ || (edgeX && dz == 1 - radius);
                var south = maxZ || (edgeX && dz == radius - 1);
                var state = FeatureRegistry.State(this.CapProvider, level, random, origin);

                state = WithSides(state, west, east, north, south);
                this.PlaceMushroomBlock(level, origin + (dx, height, dz), state);
            }
        }
    }
}
