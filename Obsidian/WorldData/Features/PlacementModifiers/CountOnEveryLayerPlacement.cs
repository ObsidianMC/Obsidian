using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// For each layer of ground (counting down from the top), places at random columns until a layer yields none.
/// </summary>
/// <remarks>
/// Like vanilla, the count is re-sampled on every iteration of the inner loop.
/// </remarks>
[ConfiguredFeatureProperty("minecraft:count_on_every_layer")]
public sealed class CountOnEveryLayerPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:count_on_every_layer";

    public required IIntProvider Count { get; init; }

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position)
    {
        var positions = new List<Vector>();
        var layer = 0;
        bool placedOnLayer;

        do
        {
            placedOnLayer = false;

            for (var i = 0; i < this.Count.Sample(random); i++)
            {
                var x = random.NextInt(16) + position.X;
                var z = random.NextInt(16) + position.Z;
                var height = context.Level.GetHeight(HeightmapType.MotionBlocking, x, z);
                var y = FindOnGroundY(context.Level, x, height, z, layer);

                if (y != int.MaxValue)
                {
                    positions.Add(new Vector(x, y, z));
                    placedOnLayer = true;
                }
            }

            layer++;
        }
        while (placedOnLayer);

        return positions;
    }

    private static int FindOnGroundY(IWorldGenLevel level, int x, int startY, int z, int targetLayer)
    {
        var layer = 0;
        var above = level.GetBlock(new Vector(x, startY, z));

        for (var y = startY; y >= level.MinY + 1; y--)
        {
            var below = level.GetBlock(new Vector(x, y - 1, z));

            if (!IsEmpty(below) && IsEmpty(above) && below.Material != Material.Bedrock)
            {
                if (layer == targetLayer)
                    return y;

                layer++;
            }

            above = below;
        }

        return int.MaxValue;
    }

    private static bool IsEmpty(IBlock block) => block.IsAir || block.Material is Material.Water or Material.Lava;
}
