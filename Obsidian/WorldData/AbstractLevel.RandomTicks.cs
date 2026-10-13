namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Vanilla <c>tickChunks</c>: <c>randomTickSpeed</c> random positions in each section of the chunks that mobs tick in, and a
    /// random tick on the block at each one. Only leaves react to it so far (<see cref="LeafDecay"/>).
    /// </summary>
    internal async ValueTask TickRandomBlocksAsync()
    {
        var speed = LevelData.GetIntegerRule("random_tick_speed", 3);
        if (speed <= 0)
            return;

        foreach (var chunk in Regions.Values.SelectMany(region => region.GeneratedChunks()).OfType<Chunk>())
        {
            if (!IsMobTicking(new VectorD(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8)))
                continue;

            var sections = chunk.Sections.Length;
            for (var section = 0; section < sections; section++)
            {
                for (var tick = 0; tick < speed; tick++)
                {
                    var position = new Vector(chunk.X * 16 + Random.Shared.Next(16), chunk.MinY + section * 16 + Random.Shared.Next(16),
                        chunk.Z * 16 + Random.Shared.Next(16));

                    if (LeafDecay.IsDecaying(chunk.GetBlock(position.X, position.Y, position.Z)))
                        await LeafDecay.RandomTickAsync(this, position);
                }
            }
        }
    }
}
