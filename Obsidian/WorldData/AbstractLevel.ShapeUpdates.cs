using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Fluids;
using System.Collections.Generic;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>updateNeighbourShapes</c> for live block changes: a block that loses its support is destroyed and drops its
/// loot, as vanilla's <c>updateOrDestroy</c> does.
/// </summary>
public abstract partial class AbstractLevel
{
    /// <summary>Vanilla's <c>Block.UPDATE_LIMIT</c>: how deep shape updates may cascade.</summary>
    internal const int ShapeUpdateLimit = 512;

    // Vanilla BlockBehaviour.UPDATE_SHAPE_ORDER.
    private static readonly BlockFace[] shapeUpdateOrder = [BlockFace.West, BlockFace.East, BlockFace.North,
        BlockFace.South, BlockFace.Down, BlockFace.Up];

    // ShapeUpdater reads blocks up to two away from the changed block (a neighbor's own neighbors).
    private const int ShapeSnapshotRadius = 2;

    /// <summary>
    /// Vanilla <c>updateNeighbourShapes</c>: each neighbor of <paramref name="position"/> updates its shape against the block
    /// there. One that becomes air is destroyed (<c>updateOrDestroy</c> → <c>destroyBlock</c>): its drops spawn and its position
    /// takes the fluid there, as vanilla <c>destroyBlock</c> does. Other changes are stored, broadcast, and cascade in turn.
    /// </summary>
    /// <remarks>
    /// The shape rules are <see cref="ShapeUpdater.UpdateShape"/>, which reads a snapshot of the blocks around the change. A
    /// destroyed block's drops don't check doTileDrops, as vanilla's <c>getDrops</c> doesn't. Drops from shape updates use no
    /// tool, so they can't be suppressed here (vanilla's <c>SuppressDrops</c> flag isn't passed by block changes yet).
    /// </remarks>
    internal async ValueTask UpdateNeighbourShapesAsync(Vector position, int updateLimit)
    {
        if (updateLimit <= 0)
            return;

        var snapshot = await ShapeSnapshot.TakeAsync(this, position, ShapeSnapshotRadius);
        var changed = snapshot.GetBlock(position);
        foreach (var face in shapeUpdateOrder)
        {
            var neighborPosition = position.Offset(face);
            var neighbor = snapshot.GetBlock(neighborPosition);
            var updated = ShapeUpdater.UpdateShape(snapshot, neighbor, neighborPosition, face.Opposite(), changed);
            if (updated.IsSameState(neighbor))
            {
                // Vanilla's updateShape schedules a leaf's distance tick under the same condition (see LeafDecay).
                if (LeafDecay.IsLeaves(neighbor) && LeafDecay.ShouldScheduleTick(neighbor, changed))
                    await ScheduleBlockUpdateAsync(new BlockUpdate(this, neighborPosition));
                continue;
            }

            if (updated.IsAir)
            {
                var drops = await BlockDrops.ForDestroyedAsync(this, neighbor, neighborPosition, await GetBlockEntityAsync(neighborPosition));
                var replacement = FluidState.Of(neighbor).CreateLegacyBlock();
                snapshot.Set(neighborPosition, replacement);
                await this.SetBlockUntrackedAsync(neighborPosition.X, neighborPosition.Y, neighborPosition.Z, replacement, true, updateLimit - 1);
                this.BroadcastBlockChange(replacement, neighborPosition);
                foreach (var stack in drops)
                    BlockDrops.SpawnDrop(this, () => Server.GetNextEntityId(), neighborPosition, stack);
            }
            else
            {
                snapshot.Set(neighborPosition, updated);
                await this.SetBlockUntrackedAsync(neighborPosition.X, neighborPosition.Y, neighborPosition.Z, updated, true, updateLimit - 1);
                this.BroadcastBlockChange(updated, neighborPosition);
            }
        }

        foreach (var scheduled in snapshot.ScheduledFluidTicks)
            this.Fluids.ScheduleShapeTick(scheduled);
    }

    /// <summary>
    /// The blocks around a changed position, read once, for <see cref="ShapeUpdater"/>. It's a snapshot: writes go to it and
    /// to the level, and reads outside the prefetched cube fail rather than guess.
    /// </summary>
    private sealed class ShapeSnapshot(AbstractLevel level, IBlock[] blocks, Vector center, int radius) : IWorldGenLevel
    {
        private readonly List<Vector> scheduledFluidTicks = [];

        public List<Vector> ScheduledFluidTicks => this.scheduledFluidTicks;

        public static async ValueTask<ShapeSnapshot> TakeAsync(AbstractLevel level, Vector center, int radius)
        {
            var side = 2 * radius + 1;
            var blocks = new IBlock[side * side * side];
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dz = -radius; dz <= radius; dz++)
                    {
                        var position = center + new Vector(dx, dy, dz);
                        blocks[Index(radius, dx, dy, dz)] = await level.GetBlockAsync(position) ?? BlocksRegistry.VoidAir;
                    }
                }
            }

            return new ShapeSnapshot(level, blocks, center, radius);
        }

        private static int Index(int radius, int dx, int dy, int dz)
        {
            var side = 2 * radius + 1;
            return ((dx + radius) * side + (dy + radius)) * side + (dz + radius);
        }

        // The snapshot index of a position, or -1 when it's outside the prefetched cube.
        private int IndexOf(Vector position)
        {
            var (dx, dy, dz) = (position.X - center.X, position.Y - center.Y, position.Z - center.Z);
            return Math.Abs(dx) <= radius && Math.Abs(dy) <= radius && Math.Abs(dz) <= radius
                ? Index(radius, dx, dy, dz)
                : -1;
        }

        public void Set(Vector position, IBlock block)
        {
            var index = this.IndexOf(position);
            if (index < 0)
                throw new InvalidOperationException($"Block {position} wasn't in the shape snapshot around {center}.");

            blocks[index] = block;
        }

        public long Seed => throw new NotSupportedException();

        public int MinY => level.MinY;

        public int Height => level.Height;

        public int SeaLevel => throw new NotSupportedException();

        public IRandomSource Random => throw new NotSupportedException();

        public IBlock GetBlock(Vector position)
        {
            var index = this.IndexOf(position);
            if (index < 0)
                throw new InvalidOperationException($"Block {position} wasn't in the shape snapshot around {center}.");

            return blocks[index];
        }

        public int GetStateId(Vector position) => this.GetBlock(position).GetHashCode();

        public bool SetBlock(Vector position, IBlock block)
        {
            this.Set(position, block);
            return true;
        }

        public bool EnsureCanWrite(Vector position) => true;

        public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

        public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

        public IBlockEntity? GetBlockEntity(Vector position) => throw new NotSupportedException();

        public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

        public void MarkForPostProcessing(Vector position) => throw new NotSupportedException();

        /// <remarks>
        /// Collected here and scheduled once the shape updates finish, since the fluid tick reads the level's blocks.
        /// </remarks>
        public void ScheduleFluidTick(Vector position) => this.scheduledFluidTicks.Add(position);
    }
}
