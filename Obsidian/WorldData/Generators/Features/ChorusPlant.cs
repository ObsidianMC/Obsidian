using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature ChorusPlant()
    {
        var supports = this.BlockTag("minecraft:supports_chorus_plant");

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (!level.GetBlock(origin).IsAir || !supports.Contains(level.GetBlock(origin + Vector.Down).RegistryId))
            {
                return false;
            }

            UpdateChorus(level, origin);
            GrowChorus(level, random, origin, origin, 0);
            return true;
        };
    }

    private static void GrowChorus(IWorldGenLevel level, IRandomSource random, Vector start, Vector root, int depth)
    {
        var height = random.NextInt(4) + 1;

        if (depth == 0)
        {
            height++;
        }

        for (var i = 0; i < height; i++)
        {
            var above = start + new Vector(0, i + 1, 0);

            if (!ChorusSpace(level, above, null))
            {
                return;
            }

            UpdateChorus(level, above);
            UpdateChorus(level, above + Vector.Down);
        }

        var tip = start + new Vector(0, height, 0);
        var branched = false;

        if (depth < 4)
        {
            var attempts = random.NextInt(4);

            if (depth == 0)
            {
                attempts++;
            }

            for (var i = 0; i < attempts; i++)
            {
                var direction = FeatureHelpers.RandomHorizontal(random);
                var branch = tip.Offset(direction);
                var withinReach = Math.Abs(branch.X - root.X) < 8 && Math.Abs(branch.Z - root.Z) < 8;
                var free = level.GetBlock(branch).IsAir && level.GetBlock(branch + Vector.Down).IsAir;

                if (!withinReach || !free || !ChorusSpace(level, branch, direction.Opposite()))
                {
                    continue;
                }

                branched = true;
                UpdateChorus(level, branch);
                UpdateChorus(level, tip);
                GrowChorus(level, random, branch, root, depth + 1);
            }
        }

        if (!branched)
        {
            level.SetBlock(tip, BlocksRegistry.ChorusFlower.WithProperty("age", "5"));
        }
    }

    private static bool ChorusSpace(IWorldGenLevel level, Vector position, BlockFace? except) =>
        FeatureHelpers.Horizontal.All(face => face == except || level.GetBlock(position.Offset(face)).IsAir);

    private static void UpdateChorus(IWorldGenLevel level, Vector position) =>
        level.SetBlock(position, ChorusPlantFeature.WithConnections(level.GetBlock, position));
}
