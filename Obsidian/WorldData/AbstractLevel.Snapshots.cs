using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    public BlockSnapshot? CaptureBlock(Vector position)
    {
        lock (this.Fluids.TickLock)
        {
            if (this.IsOutsideBuildHeight(position.Y)
                || this.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not IChunk chunk
                || !chunk.IsGenerated || !this.Fluids.IsResident(chunk) || chunk is Chunk { EntitiesUnloaded: true })
                return null;

            var entity = chunk.GetBlockEntity(position.X, position.Y, position.Z);
            return new BlockSnapshot(chunk.GetBlock(position), entity is null ? null : BlockEntityNbt.Save(entity));
        }
    }

    public async ValueTask<bool> RestoreBlockAsync(Vector position, BlockSnapshot snapshot)
    {
        BlockEntity? removed;
        lock (this.Fluids.TickLock)
        {
            if (this.IsOutsideBuildHeight(position.Y)
                || this.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not IChunk chunk
                || !chunk.IsGenerated || !this.Fluids.IsResident(chunk) || chunk is Chunk { EntitiesUnloaded: true })
                return false;

            var old = chunk.GetBlock(position);
            var type = snapshot.Block.HasBlockEntity() ? snapshot.Block.BlockEntityType() : null;
            var prepared = type is null ? null : snapshot.Data is null
                ? BlockEntityNbt.Create(type, position, logger: this.Logger)
                : BlockEntityNbt.Load(DataBlockEntity.Copy(snapshot.Data), type, position);
            removed = this.UpdateBlockEntity(chunk, position, snapshot.Block, prepared);
            chunk.SetBlock(position.X, position.Y, position.Z, snapshot.Block);
            this.CompleteBlockEntityPlacement(chunk, prepared);
            this.AfterBlockSet(position, old, snapshot.Block,
                BlockUpdateFlags.Clients | BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        if (removed is not null)
            await this.RunBlockEntityRemovalAsync(removed, dropContents: false);
        return true;
    }
}
