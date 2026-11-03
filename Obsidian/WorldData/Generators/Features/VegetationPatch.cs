using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature VegetationPatch(JsonElement json, bool waterlogged)
    {
        var settings = new PatchSettings(
            this.Blocks(json.GetProperty("replaceable")),
            this.Provider(json.GetProperty("ground_state")),
            this.Placed(json.GetProperty("vegetation_feature")),
            json.GetProperty("surface").GetString() == "floor" ? BlockFace.Down : BlockFace.Up,
            this.Int(json.GetProperty("depth")),
            json.GetProperty("extra_bottom_block_chance").GetSingle(),
            json.GetProperty("vertical_range").GetInt32(),
            json.GetProperty("vegetation_chance").GetSingle(),
            this.Int(json.GetProperty("xz_radius")),
            json.GetProperty("extra_edge_column_chance").GetSingle());

        return (context, random, origin) =>
        {
            var level = context.Level;
            var radiusX = settings.Radius(random) + 1;
            var radiusZ = settings.Radius(random) + 1;
            var ground = PatchGround(level, random, origin, radiusX, radiusZ, settings);

            if (waterlogged)
            {
                var land = ground;
                ground = PatchWater(level, land);
                ReturnPatchList(land);
            }

            foreach (var position in ground)
            {
                if (settings.VegetationChance <= 0.0f || random.NextFloat() >= settings.VegetationChance)
                {
                    continue;
                }

                var plantPosition = position.Offset(settings.Toward.Opposite());

                if (waterlogged)
                {
                    // The waterlogged variant passes ground.below() to the base implementation.
                    plantPosition += Vector.Down;
                }

                var placed = settings.Vegetation.Place(context, random, plantPosition);

                if (waterlogged && placed)
                {
                    var plant = level.GetBlock(position);

                    if (plant.GetProperty("waterlogged") == "false")
                    {
                        level.SetBlock(position, plant.WithProperty("waterlogged", "true"));
                    }
                }
            }

            var any = ground.Count > 0;
            ReturnPatchList(ground);
            return any;
        };
    }

    // Position lists no patch is using, per thread: a patch's vegetation may place another patch inside it.
    [ThreadStatic]
    private static Stack<List<Vector>>? freePatchLists;

    private static List<Vector> RentPatchList()
    {
        var free = freePatchLists ??= new Stack<List<Vector>>();
        return free.TryPop(out var positions) ? positions : [];
    }

    private static void ReturnPatchList(List<Vector> positions)
    {
        positions.Clear();
        freePatchLists!.Push(positions);
    }

    private static readonly BlockFace[] waterWalls =
        [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West, BlockFace.Down];

    private static List<Vector> PatchGround(
        IWorldGenLevel level,
        IRandomSource random,
        Vector origin,
        int radiusX,
        int radiusZ,
        PatchSettings settings)
    {
        var ground = RentPatchList();
        var away = settings.Toward.Opposite();

        for (var dx = -radiusX; dx <= radiusX; dx++)
        {
            var edgeX = Math.Abs(dx) == radiusX;

            for (var dz = -radiusZ; dz <= radiusZ; dz++)
            {
                var edgeZ = Math.Abs(dz) == radiusZ;

                if (edgeX && edgeZ)
                {
                    continue;
                }

                if ((edgeX || edgeZ) && (settings.EdgeChance == 0.0f || random.NextFloat() > settings.EdgeChance))
                {
                    continue;
                }

                var position = origin + new Vector(dx, 0, dz);

                for (var i = 0; level.GetBlock(position).IsAir && i < settings.Range; i++)
                {
                    position = position.Offset(settings.Toward);
                }

                for (var i = 0; !level.GetBlock(position).IsAir && i < settings.Range; i++)
                {
                    position = position.Offset(away);
                }

                var surface = position.Offset(settings.Toward);

                if (!level.GetBlock(position).IsAir || !level.GetBlock(surface).IsFaceSturdy(away))
                {
                    continue;
                }

                var depth = settings.Depth(random);

                if (settings.BottomChance > 0.0f && random.NextFloat() < settings.BottomChance)
                {
                    depth++;
                }

                if (PatchColumn(level, random, surface, depth, settings))
                {
                    ground.Add(surface);
                }
            }
        }

        var ordered = RentPatchList();
        FeatureHelpers.JavaHashSetOrder(ground, ordered);
        ReturnPatchList(ground);
        return ordered;
    }

    private static bool PatchColumn(
        IWorldGenLevel level, IRandomSource random, Vector position, int depth, PatchSettings settings)
    {
        for (var i = 0; i < depth; i++)
        {
            var state = State(settings.Ground, level, random, position);
            var existing = level.GetBlock(position);

            // A matching block consumes the iteration and provider draws without advancing the column.
            if (state.RegistryId == existing.RegistryId)
            {
                continue;
            }

            if (!settings.Replaceable.Contains(existing.RegistryId))
            {
                return i != 0;
            }

            level.SetBlock(position, state);
            position = position.Offset(settings.Toward);
        }

        return true;
    }

    private static List<Vector> PatchWater(IWorldGenLevel level, List<Vector> ground)
    {
        var enclosed = RentPatchList();

        foreach (var position in ground)
        {
            if (IsWalledIn(level, position))
            {
                enclosed.Add(position);
            }
        }

        var retained = RentPatchList();
        FeatureHelpers.JavaHashSetOrder(enclosed, retained);
        ReturnPatchList(enclosed);

        foreach (var position in retained)
        {
            level.SetBlock(position, BlocksRegistry.Water);
        }

        return retained;
    }

    private static bool IsWalledIn(IWorldGenLevel level, Vector position)
    {
        foreach (var face in waterWalls)
        {
            if (!level.GetBlock(position.Offset(face)).IsFaceSturdy(face.Opposite()))
                return false;
        }

        return true;
    }

    private sealed record PatchSettings(
        HashSet<int> Replaceable,
        StateProvider Ground,
        PlacedFeature Vegetation,
        BlockFace Toward,
        IntProvider Depth,
        float BottomChance,
        int Range,
        float VegetationChance,
        IntProvider Radius,
        float EdgeChance);
}
