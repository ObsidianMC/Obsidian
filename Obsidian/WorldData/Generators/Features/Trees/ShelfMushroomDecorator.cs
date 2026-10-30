using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// 26.3's shelf mushrooms. On a standing tree they grow on two perpendicular sides of the logs one to four blocks
/// above its base, at most one per log and never on top of another; on a fallen log (logs all at one height) they grow
/// on its two long sides, never beside another. Each side draws a quarter chance first. Ported from the 26.3 bytecode
/// for its draw order: the decorator's probability, the sides, then per log and side the chance and the mushroom's age.
/// </summary>
internal sealed class ShelfMushroomDecorator(JsonElement json) : TreeDecorator
{
    private const float PerSideChance = 0.25f;

    private readonly float probability = json.GetProperty("probability").GetSingle();

    public override void Place(TreeDecoration context)
    {
        var random = context.Random;

        if (random.NextFloat() >= this.probability)
        {
            return;
        }

        var logs = context.Logs;

        if (logs.Count == 0)
        {
            return;
        }

        var fallen = logs[0].Y == logs[^1].Y;

        if (fallen)
        {
            PlaceOnFallenLog(context, logs, random);
        }
        else
        {
            PlaceOnStandingTree(context, logs, random);
        }
    }

    private static void PlaceOnStandingTree(TreeDecoration context, IReadOnlyList<Vector> logs, IRandomSource random)
    {
        var first = FeatureHelpers.RandomHorizontal(random);
        BlockFace[] sides = [first, first.ClockWise()];
        var baseY = logs[0].Y;

        foreach (var log in logs)
        {
            var above = log.Y - baseY;

            if (above < 1 || above > 4)
            {
                continue;
            }

            foreach (var side in sides)
            {
                if (random.NextFloat() > PerSideChance)
                {
                    continue;
                }

                if (TryPlaceOnStandingTree(context, log, side, random))
                {
                    break;
                }
            }
        }
    }

    private static bool TryPlaceOnStandingTree(TreeDecoration context, Vector log, BlockFace side, IRandomSource random)
    {
        var position = log.Offset(side);

        if (!IsReplaceable(context, position) || HasShelfMushroom(context, position + Vector.Down))
        {
            return false;
        }

        PlaceMushroom(context, position, side, random);
        return true;
    }

    private static void PlaceOnFallenLog(TreeDecoration context, IReadOnlyList<Vector> logs, IRandomSource random)
    {
        var alongX = logs[0].X != logs[^1].X;
        BlockFace[] sides = alongX ? [BlockFace.North, BlockFace.South] : [BlockFace.East, BlockFace.West];

        foreach (var log in logs)
        {
            foreach (var side in sides)
            {
                if (random.NextFloat() > PerSideChance)
                {
                    continue;
                }

                var position = log.Offset(side);

                if (!IsReplaceable(context, position))
                {
                    continue;
                }

                if (HasAdjacentShelfMushroom(context, position) || HasAdjacentShelfMushroom(context, log))
                {
                    continue;
                }

                PlaceMushroom(context, position, side, random);
            }
        }
    }

    private static void PlaceMushroom(TreeDecoration context, Vector position, BlockFace side, IRandomSource random)
    {
        var age = random.NextInt(2);
        var mushroom = BlocksRegistry.Get("minecraft:shelf_mushroom")
            .WithProperty("age", age)
            .WithProperty("facing", FeatureHelpers.FaceName(side));

        context.SetBlock(position, mushroom);
    }

    // Replaceable, and neither water nor beside water horizontally.
    private static bool IsReplaceable(TreeDecoration context, Vector position)
    {
        var level = context.Level;

        if (!level.GetBlock(position).CanBeReplaced())
        {
            return false;
        }

        BlockFace[] neighbors = [BlockFace.East, BlockFace.West, BlockFace.North, BlockFace.South];
        var waterHere = IsWater(level.GetBlock(position));
        var waterNearby = neighbors.Any(face => IsWater(level.GetBlock(position.Offset(face))));

        return !waterHere && !waterNearby;
    }

    private static bool IsWater(IBlock block) => block.UnlocalizedName == "minecraft:water";

    private static bool HasShelfMushroom(TreeDecoration context, Vector position) =>
        context.Level.GetBlock(position).UnlocalizedName == "minecraft:shelf_mushroom";

    private static bool HasAdjacentShelfMushroom(TreeDecoration context, Vector position) =>
        FeatureHelpers.Horizontal.Any(face => HasShelfMushroom(context, position.Offset(face)));
}
