using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Registries;
using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's ServerLevel.randValue, which picks the blocks random ticks reach.
    private int randomTickValue = Random.Shared.Next();

    /// <summary>
    /// The random plants grow with on the level's tick, from random ticks and bone meal, like vanilla's level random.
    /// Tests set their own.
    /// </summary>
    internal IRandomSource GrowthRandom { get; set; } = new LegacyRandomSource(Random.Shared.NextInt64());

    /// <summary>
    /// Where random ticks pick their next block from (vanilla's <c>randValue</c>), which moves on once for each block
    /// they pick.
    /// </summary>
    internal int RandomTickValue => this.randomTickValue;

    /// <summary>
    /// Random-ticks the chunks that tick, like vanilla's <c>ServerLevel.tickChunk</c>: the game rule's number of
    /// blocks in each section that holds a block or fluid that random-ticks, of which the plants that grow on random
    /// ticks grow (see <see cref="Growth"/>), plugin blocks that random-tick run their hook and lava draws its fire's
    /// chances.
    /// </summary>
    /// <remarks>
    /// Each chunk ticks under the level's <see cref="Fluids.LevelFluids.TickLock"/>, which players' block writes take
    /// too, so that a plant grows from the block it read and a tree into the space it checked.
    /// </remarks>
    internal void TickRandomBlocks()
    {
        var speed = LevelData.GetIntegerRule("random_tick_speed", 3);
        if (speed <= 0)
            return;

        var players = this.SimulatingPlayerPositions();
        foreach (var chunk in this.RandomTickingChunks(players))
        {
            lock (this.Fluids.TickLock)
                this.TickChunkBlocks(chunk, speed, players);
        }
    }

    /// <summary>
    /// Uses bone meal on the block at <paramref name="position"/>, like vanilla's <c>BoneMealItem.useOn</c>, from the
    /// <paramref name="player"/>'s inventory <paramref name="slot"/> (or from nowhere, without a player).
    /// </summary>
    /// <remarks>
    /// The bone meal is used on the level's next tick, with the block there then, so that uses queued together each
    /// see the block the one before left. If it still takes bone meal, it grows when its chance comes up, players see
    /// the bone meal's particles and hear its sound, and the player's bone meal is used up unless they're in creative
    /// mode. If not, or the player no longer holds bone meal in the slot, nothing happens and nothing is used up.
    /// </remarks>
    /// <returns>Whether the block takes bone meal now, so that the use goes ahead.</returns>
    internal bool UseBoneMeal(Vector position, IPlayer? player = null, int slot = 0)
    {
        if (GameEventBlock(position) is not IBlock block || !Growth.IsValidBonemealTarget(this, position, block))
            return false;

        EnqueueEntityAction(() => this.ApplyBoneMealAsync(position, player, slot));
        return true;
    }

    // Vanilla's BoneMealItem.growCrop: a block that takes bone meal uses it up, whether it grows or not.
    private async ValueTask ApplyBoneMealAsync(Vector position, IPlayer? player, int slot)
    {
        if (player is not null && player.Inventory.GetItem(slot) is not { Count: > 0, Holder.Id: ItemIds.BoneMeal })
            return;

        if (!this.TryGrowWithBoneMeal(position))
            return;

        BroadcastLevelEvent(Growth.BoneMealEvent, position, Growth.BoneMealParticles);

        if (player is null || player.GameMode == GameMode.Creative)
            return;

        player.Inventory.RemoveItem(slot, 1);
        await player.SendInventorySlotAsync(slot);
    }

    // Under the lock players' block writes take, like random ticks.
    private bool TryGrowWithBoneMeal(Vector position)
    {
        lock (this.Fluids.TickLock)
        {
            if (GameEventBlock(position) is not IBlock block || !Growth.IsValidBonemealTarget(this, position, block))
                return false;

            Growth.ApplyBonemeal(this, position, block, this.GrowthRandom);
            return true;
        }
    }

    // Vanilla's tickChunk for blocks: each section that holds something that random-ticks gets the speed's blocks,
    // from the position sequence, and both the block and the fluid of the state read there tick. Sections without
    // one draw no position, as in vanilla, which keeps the sequence vanilla's.
    private void TickChunkBlocks(Chunk chunk, int speed, List<VectorD> players)
    {
        var sections = chunk.Sections;
        for (var index = 0; index < sections.Length; index++)
        {
            if (sections[index] is not ChunkSection { IsRandomlyTicking: true } section)
                continue;

            for (var tick = 0; tick < speed; tick++)
            {
                // Vanilla's getBlockRandomPos: x, z and y from a linear congruential sequence.
                this.randomTickValue = this.randomTickValue * 3 + 1013904223;
                var bits = this.randomTickValue >> 2;
                var x = bits & 15;
                var y = bits >> 16 & 15;
                var z = bits >> 8 & 15;

                var block = section.GetBlock(x, y, z);
                var position = new Vector(chunk.X * 16 + x, chunk.MinY + index * 16 + y, chunk.Z * 16 + z);

                if (Growth.IsRandomlyTicking(block))
                    Growth.RandomTick(this, position, block, this.GrowthRandom);

                this.BlockUpdates.RandomTick(position, block);

                if (block.GetFluid() is FluidKind.Lava or FluidKind.FlowingLava)
                    this.RandomTickLava(position, players);
            }
        }
    }

    // The positions of the players chunks tick around, read once a tick: those that aren't spectators, as for
    // IsMobTicking.
    private List<VectorD> SimulatingPlayerPositions()
    {
        var positions = new List<VectorD>();
        foreach (var (_, player) in this.Players)
        {
            if (player.GameMode != GameMode.Spectator)
                positions.Add(player.Position);
        }

        return positions;
    }

    // Simulated chunks, from players or tickets, whose eight neighbours are complete too, so growth never reaches
    // a chunk that isn't there. Tickets use the same random-tick path as players.
    private List<Chunk> RandomTickingChunks(List<VectorD> players)
    {
        var distance = (int)Configuration.SimulationDistance;
        var seen = new HashSet<long>();
        var chunks = new List<Chunk>();

        foreach (var player in players)
        {
            var (centerX, centerZ) = Region.ChunkOf(player);
            for (var x = centerX - distance; x <= centerX + distance; x++)
            {
                for (var z = centerZ - distance; z <= centerZ + distance; z++)
                {
                    if (!seen.Add(NumericsHelper.IntsToLong(x, z)))
                        continue;

                    if (GetLoadedChunk(x, z) is Chunk chunk && this.HasLoadedNeighbours(x, z))
                        chunks.Add(chunk);
                }
            }
        }

        foreach (var key in Volatile.Read(ref this.ticketChunks).Ticking)
        {
            if (!seen.Add(key))
                continue;

            NumericsHelper.LongToInts(key, out var x, out var z);
            if (this.GetLoadedChunk(x, z) is Chunk chunk && this.HasLoadedNeighbours(x, z))
                chunks.Add(chunk);
        }

        return chunks;
    }

    private bool HasLoadedNeighbours(int chunkX, int chunkZ)
    {
        for (var x = chunkX - 1; x <= chunkX + 1; x++)
        {
            for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
            {
                if (GetLoadedChunk(x, z) is null)
                    return false;
            }
        }

        return true;
    }

    // Vanilla's LavaFluid.randomTick, which sets fire to flammable blocks around lava when a player is within the
    // fire spread game rule's radius. Obsidian has no fire spread or flammability yet, so nothing catches fire, but
    // the draws are vanilla's, in its order, ending where vanilla's end when nothing nearby is flammable, so that the
    // level's random stays in step with vanilla's.
    private void RandomTickLava(Vector position, List<VectorD> players)
    {
        if (!this.CanSpreadFireAround(position, players))
            return;

        var random = this.GrowthRandom;
        var climbs = random.NextInt(3);
        if (climbs > 0)
        {
            var at = position;
            for (var climb = 0; climb < climbs; climb++)
            {
                var dx = random.NextInt(3) - 1;
                var dz = random.NextInt(3) - 1;
                at += new Vector(dx, 1, dz);

                // Vanilla sets fire in air next to a flammable block and stops; a block in the way stops it too.
                if (GameEventBlock(at) is not IBlock block || block.BlocksMotion())
                    return;
            }

            return;
        }

        for (var spot = 0; spot < 3; spot++)
        {
            var dx = random.NextInt(3) - 1;
            var dz = random.NextInt(3) - 1;

            // Vanilla sets fire on top of a flammable block here; it stops at a chunk that isn't loaded.
            if (GameEventBlock(position + new Vector(dx, 0, dz)) is null)
                return;
        }
    }

    // Vanilla's canSpreadFireAround: the game rule's radius around a player that isn't a spectator, or everywhere.
    private bool CanSpreadFireAround(Vector position, List<VectorD> players)
    {
        var radius = LevelData.GetIntegerRule("fire_spread_radius_around_player", 128);
        if (radius == -1)
            return true;

        foreach (var player in players)
        {
            if ((player - (VectorD)position).Magnitude < radius)
                return true;
        }

        return false;
    }
}
