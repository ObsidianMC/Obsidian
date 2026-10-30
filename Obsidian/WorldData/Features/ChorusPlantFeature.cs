using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A full-grown chorus plant on end stone, like vanilla's ChorusPlantFeature (<c>ChorusFlowerBlock.generatePlant</c>).
/// </summary>
[ConfiguredFeatureClass("minecraft:chorus_plant")]
public sealed class ChorusPlantFeature : ConfiguredFeatureBase
{
    private const int MaxHorizontalReach = 8;

    public override string Type => "minecraft:chorus_plant";

    private static IBlock ChorusPlant => field ??= BlocksRegistry.ChorusPlant;

    private static IBlock GrownFlower => field ??= BlocksRegistry.ChorusFlower.WithProperty("age", 5);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin)
            || !level.GetBlock(origin).IsAir
            || !level.GetBlock(origin + Vector.Down).Is(BlockIds.EndStone))
        {
            return false;
        }

        level.SetBlock(origin, WithConnections(level.GetBlock, origin));
        GrowTreeRecursive(level, origin, context.Random, origin, 0);
        return true;
    }

    private static void GrowTreeRecursive(IWorldGenLevel level, Vector position, IRandomSource random, Vector root, int depth)
    {
        var height = random.NextInt(4) + 1;
        if (depth == 0)
            height++;

        for (var i = 0; i < height; i++)
        {
            var above = position + new Vector(0, i + 1, 0);
            if (!AllNeighborsEmpty(level, above, null))
                return;

            level.SetBlock(above, WithConnections(level.GetBlock, above));
            level.SetBlock(above + Vector.Down, WithConnections(level.GetBlock, above + Vector.Down));
        }

        var branched = false;
        if (depth < 4)
        {
            var branches = random.NextInt(4);
            if (depth == 0)
                branches++;

            for (var i = 0; i < branches; i++)
            {
                var direction = FeatureHelpers.RandomHorizontal(random);
                var branch = (position + new Vector(0, height, 0)).Offset(direction);
                if (Math.Abs(branch.X - root.X) < MaxHorizontalReach && Math.Abs(branch.Z - root.Z) < MaxHorizontalReach
                    && level.GetBlock(branch).IsAir && level.GetBlock(branch + Vector.Down).IsAir
                    && AllNeighborsEmpty(level, branch, direction.Opposite()))
                {
                    branched = true;
                    level.SetBlock(branch, WithConnections(level.GetBlock, branch));

                    var back = branch.Offset(direction.Opposite());
                    level.SetBlock(back, WithConnections(level.GetBlock, back));
                    GrowTreeRecursive(level, branch, random, root, depth + 1);
                }
            }
        }

        if (!branched)
            level.SetBlock(position + new Vector(0, height, 0), GrownFlower);
    }

    private static bool AllNeighborsEmpty(IWorldGenLevel level, Vector position, BlockFace? except)
    {
        foreach (var face in FeatureHelpers.Horizontal)
        {
            if (face != except && !level.GetBlock(position.Offset(face)).IsAir)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>ChorusPlantBlock.getStateWithConnections</c>: connects to neighboring chorus plants and flowers, and down to end
    /// stone.
    /// </summary>
    internal static IBlock WithConnections(Func<Vector, IBlock> blockAt, Vector position)
    {
        var state = ChorusPlant;
        foreach (var face in FeatureHelpers.Directions)
        {
            var neighbor = blockAt(position.Offset(face));
            var connects = neighbor.RegistryId is BlockIds.ChorusPlant or BlockIds.ChorusFlower
                || face == BlockFace.Down && neighbor.Is(BlockIds.EndStone);
            state = state.WithProperty(FeatureHelpers.FaceName(face), connects);
        }

        return state;
    }
}
