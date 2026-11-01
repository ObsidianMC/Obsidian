using Obsidian.WorldData.Lighting;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Reads the light threshold mushroom survival needs before the final lighting pass.</summary>
internal sealed class CompletionLight(IReadOnlyDictionary<(int X, int Z), IChunk> area, bool hasSky)
{
    private readonly Dictionary<(int X, int Z), int> sky = [];

    public int MushroomBrightness(Vector position)
    {
        // Survival distinguishes light below 13. Even a level-15 source cannot reach that threshold after
        // more than two attenuation steps. Walk toward sources with the light engine's faces and opacity.
        var pending = new Queue<(Vector Position, int Loss)>();
        var visited = new Dictionary<Vector, int> { [position] = 0 };
        pending.Enqueue((position, 0));
        while (pending.TryDequeue(out var current))
        {
            var chunk = area.GetValueOrDefault((current.Position.X >> 4, current.Position.Z >> 4));
            if (chunk is null || current.Position.Y < chunk.MinY || current.Position.Y >= chunk.MinY + chunk.Height)
                continue;
            var block = chunk.GetBlock(current.Position.X, current.Position.Y, current.Position.Z);
            if (block.LightEmission() - current.Loss >= 13)
                return 13;
            if (hasSky)
            {
                var column = (current.Position.X, current.Position.Z);
                if (!this.sky.TryGetValue(column, out var lowest))
                {
                    lowest = SkyLightSources.LowestSourceY(chunk, column.Item1 & 15, column.Item2 & 15);
                    this.sky[column] = lowest;
                }
                if (current.Position.Y >= lowest)
                    return 13;
            }

            var loss = current.Loss + Math.Max(1, block.LightBlock());
            if (loss > 2)
                continue;
            foreach (var face in new[] { BlockFace.Down, BlockFace.Up, BlockFace.North,
                BlockFace.South, BlockFace.West, BlockFace.East })
            {
                var neighborPosition = current.Position.Offset(face);
                var neighborChunk = area.GetValueOrDefault((neighborPosition.X >> 4, neighborPosition.Z >> 4));
                if (neighborChunk is null || visited.GetValueOrDefault(neighborPosition, int.MaxValue) <= loss)
                    continue;
                if (neighborPosition.Y < neighborChunk.MinY || neighborPosition.Y >= neighborChunk.MinY + neighborChunk.Height)
                    continue;
                var neighbor = neighborChunk.GetBlock(neighborPosition.X, neighborPosition.Y, neighborPosition.Z);
                if (BlockLight.LightShapesOcclude(neighbor, block, face.Opposite()))
                    continue;
                visited[neighborPosition] = loss;
                pending.Enqueue((neighborPosition, loss));
            }
        }
        return 0;
    }
}
