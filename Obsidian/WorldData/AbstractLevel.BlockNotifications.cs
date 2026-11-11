using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// The level as the block notification pipeline and the redstone engine see it. Reads and writes never load chunks: a
/// position in an unloaded chunk reads as unpowered, and a write there is dropped, like vanilla's <c>setBlock</c>.
/// </summary>
public abstract partial class AbstractLevel : IBlockChangeWorld, IRedstoneLevel, IBlockTickLevel, IContactLevel
{
    // Vanilla's flag 2 (clients only), as the redstone writes use it: no neighbor updates, but shape updates still run
    // (vanilla setBlock only skips them with flag 16), so an observer watching the block sees the change.
    private const BlockUpdateFlags ClientsOnly = BlockUpdateFlags.Clients | BlockUpdateFlags.SkipOnPlace;

    /// <summary>The neighbor reactions of the redstone engine, keyed by block.</summary>
    internal NeighborBehaviors Neighbors { get; }

    /// <summary>The redstone signal behaviors, keyed by block class.</summary>
    internal RedstoneBehaviors RedstoneRules { get; }

    /// <summary>The queue that runs neighbor reactions after each committed block change.</summary>
    internal BlockNotificationPipeline BlockNotifications { get; }

    /// <summary>The installed redstone engine, with its tripwires and targets.</summary>
    internal RedstoneInstall RedstoneInstall { get; }

    /// <summary>The pressure plates, which the entity contact pass presses.</summary>
    internal PressurePlates Plates => this.RedstoneInstall.Contacts.Plates;

    /// <summary>
    /// The hooks of the blocks entities touch, run for each entity straight after its tick (see
    /// <see cref="EntityContactPass"/>).
    /// </summary>
    internal EntityContactPass EntityContacts { get; }

    IBlockLevel IContactLevel.Blocks => this.BlockUpdates;

    ILevel IContactLevel.Level => this;

    RedstoneContacts IContactLevel.Redstone => this.RedstoneInstall.Contacts;

    Microsoft.Extensions.Logging.ILogger IContactLevel.Logger => this.Logger;

    int IContactLevel.MinY => this.MinY;

    int IContactLevel.Height => this.Height;

    // Straight from the regions, not through GetRegionForChunk's per-thread cache, which allocates whenever the region
    // changes: the pass goes from entity to entity, and those in different regions would make it allocate each time.
    Chunk? IContactLevel.GetContactChunk(int chunkX, int chunkZ)
    {
        var shift = Region.CubicRegionSizeShift;
        var key = NumericsHelper.IntsToLong(chunkX >> shift, chunkZ >> shift);
        if (!this.Regions.TryGetValue(key, out var found) || found is not Region region)
            return null;

        var x = NumericsHelper.Modulo(chunkX, 32);
        var z = NumericsHelper.Modulo(chunkZ, 32);
        return region.GetLoadedChunk(x, z) as Chunk;
    }

    /// <summary>The level's redstone engine (signal reads).</summary>
    internal RedstoneEngine RedstoneEngine => this.RedstoneInstall.Engine;

    int IBlockChangeWorld.MinY => this.MinY;

    int IBlockChangeWorld.Height => this.Height;

    IBlock? IBlockChangeWorld.GetLoadedBlock(Vector position)
    {
        if (this.IsOutsideBuildHeight(position.Y))
            return BlocksRegistry.VoidAir;

        return this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord())?.GetBlock(position.X, position.Y, position.Z);
    }

    int IBlockChangeWorld.GetRawBrightness(Vector position) => this.Fluids.GetRawBrightness(position);

    IBlock? IBlockChangeWorld.UpdateShape(
        Vector position, IBlock state, BlockFace face, Vector neighborPosition, IBlock neighbor) =>
        ShapeDispatch.LiveOwner(state.RegistryId) is not null
            ? this.BlockUpdates.UpdateShape(position, state, face, neighborPosition, neighbor)
            : null;

    ValueTask IBlockChangeWorld.SetBlockAsync(Vector position, IBlock block) => this.WriteAsync(position, block, BlockUpdateFlags.All);

    void IBlockChangeWorld.DropAndRemove(Vector position, IBlock block, bool playEffect)
    {
        // Wire and rails are removed through dropResources/removeBlock, which emits no break effect.
        if (block.BlockClass() == "RedstoneWireBlock" || Rails.IsRail(block))
            playEffect = false;

        this.QueueBlockDestruction(position, true, null, replacement: null, playEffect,
            stillUnsupported: current => current.BlockClass() switch
            {
                "RedstoneTorchBlock" => this.BlockUpdates.GetBlock(position + Vector.Down).IsTopCenterSturdy() == false,
                _ when Rails.IsRail(current) => RedstoneRail.IsUnsupported(position, current, position => ((IBlockChangeWorld)this).GetLoadedBlock(position)),
                _ => this.BlockUpdates.IsUnsupported(position, current)
            });
    }

    void IBlockChangeWorld.ScheduleFluidTick(Vector position) => this.Fluids.ScheduleShapeFluidTick(position);

    async ValueTask IBlockChangeWorld.NotifyNeighborsAsync(Vector position)
    {
        this.BlockNotifications.EnqueueNeighbors(position);
        await this.BlockNotifications.DrainAsync();
    }

    void IRedstoneLevel.BroadcastLevelEvent(int type, Vector position, int data) => this.BroadcastLevelEvent(type, position, data);

    void IRedstoneLevel.QueueBlockEvent(Vector position, int blockId, int type, int data,
        Func<ValueTask<bool>> trigger) => this.QueueBlockEvent(position, blockId, type, data, trigger);

    // Vanilla dropResources/removeBlock for a piston-destroyed block. The drops are captured here, on the simulation path,
    // so the caller can overwrite the cell afterwards; the queued path would run after the move had replaced the block.
    async ValueTask IRedstoneLevel.DestroyBlockNowAsync(Vector position, IBlock block, bool playEffect)
    {
        if (block.IsAir)
            return;

        if (block.BlockClass() == "RedstoneWireBlock" || Rails.IsRail(block))
            playEffect = false;

        try
        {
            await this.DestroyCapturedBlockAsync(position, block, this.GetResidentBlockEntity(position), true, null, null,
                playEffect, false, null);
        }
        catch (Exception exception)
        {
            BlockDestructionFailed(this.Logger, exception, block.UnlocalizedName, position);
        }
    }

    void IBlockChangeWorld.PlaySound(Vector position, string sound, float volume, float pitch, IPlayer? except) =>
        this.BlockUpdates.PlaySound(position, sound, volume, pitch, except);


    long IRedstoneLevel.GameTime => this.Fluids.Clock.GameTime;

    IRandomSource IRedstoneLevel.Random => this.GrowthRandom;

    // Vanilla Level.soundSeedGenerator is independent of gameplay randomness, including piston pitch.
    internal IRandomSource SoundSeedRandom { get; set; } = new LegacyRandomSource(Random.Shared.NextInt64());

    /// <summary>Draws a packet seed independently of gameplay, including callers outside the level tick.</summary>
    internal long NextSoundSeed()
    {
        var random = this.SoundSeedRandom;
        lock (random)
        {
            return random.NextLong();
        }
    }

    long IRedstoneLevel.DayTime => this.LevelData.DayTime;

    int IRedstoneLevel.GetSkyLight(Vector position) =>
        this.IsOutsideBuildHeight(position.Y) ? 0 :
        this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord())?.GetLightLevel(position.X, position.Y, position.Z, LightType.Sky) ?? 0;

    int IRedstoneLevel.TrappedChestViewers(Vector position) => this.TrappedChestViewers(position);

    bool IRedstoneLevel.HasAnalogOutput(IBlock block)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.HasAnalogOutput(block);
    }

    int IRedstoneLevel.GetAnalogOutput(Vector position, IBlock block, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.AnalogOutputOf(block, position, direction);
    }

    void IRedstoneLevel.ScheduleBlockTick(Vector position, IBlock block, int delay, Obsidian.API.Blocks.TickPriority priority) =>
        this.ScheduleBlockTick(position, block, delay, priority);

    bool IRedstoneLevel.IsBlockTickScheduled(Vector position, string block)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.Ticks.IsScheduled(position, block);
    }

    void IRedstoneLevel.EnqueueSimulation(Func<ValueTask> action) => this.EnqueueSimulationAction(action);

    bool IRedstoneLevel.TntExplodes => this.LevelData.GetBooleanRule("tnt_explodes");

    void IRedstoneLevel.SetCrafterTriggered(Vector position, bool triggered)
    {
        if (this.BlockUpdates.GetBlockEntity(position) is CrafterBlockEntity crafter)
        {
            crafter.Crafter.Triggered = triggered;
            crafter.SetChanged();
        }
    }

    async ValueTask IRedstoneLevel.NotifyNeighborAsync(Vector target, Vector source)
    {
        this.BlockNotifications.EnqueueNeighbor(target, source);
        await this.BlockNotifications.DrainAsync();
    }

    async ValueTask IRedstoneLevel.NotifyComparatorsAroundAsync(Vector position)
    {
        this.EnqueueComparatorsAround(position);
        await this.BlockNotifications.DrainAsync();
    }

    IEnumerable<Obsidian.API.IEntity> IRedstoneLevel.EntitiesIn(VectorD min, VectorD max) => this.EntitiesIn(min, max);

    /// <summary>
    /// The entities whose boxes overlap the box from <paramref name="min"/> to <paramref name="max"/>, among those near it.
    /// </summary>
    internal IEnumerable<IEntity> EntitiesIn(VectorD min, VectorD max)
    {
        var center = (min + max) / 2;
        foreach (var entity in this.GetEntitiesInRange(center, 4))
        {
            var box = entity is Entities.Entity concrete ? concrete.CurrentBox : entity.BoundingBox;
            if (box.Min.X < max.X && box.Max.X > min.X
                && box.Min.Y < max.Y && box.Max.Y > min.Y
                && box.Min.Z < max.Z && box.Max.Z > min.Z)
            {
                yield return entity;
            }
        }
    }

    void IRedstoneLevel.FireDispenser(Vector position, IBlock state, bool dropper) =>
        this.EnqueueEntityAction(() => RedstoneDispenser.FireAsync(this, position, state, dropper));

    void IRedstoneLevel.CraftCrafter(Vector position) =>
        this.EnqueueEntityAction(() => RedstoneCrafter.CraftAsync(this, position));

    void IRedstoneLevel.PrimeTnt(Vector position) =>
        this.EnqueueEntityAction(() =>
        {
            Entities.PrimedTnt.Prime(this, position, owner: null);
            return ValueTask.CompletedTask;
        });

    ValueTask IRedstoneLevel.WriteAsync(Vector position, IBlock block, bool notifyNeighbors) =>
        this.WriteAsync(position, block, notifyNeighbors ? BlockUpdateFlags.All : ClientsOnly);

    ValueTask IRedstoneLevel.WriteAsync(Vector position, IBlock block, BlockUpdateFlags flags) =>
        this.WriteAsync(position, block, flags);

    async ValueTask IRedstoneLevel.UpdateNeighborShapesAsync(Vector position)
    {
        // Vanilla's direct shape order, independent of the signal-query direction order.
        BlockFace[] faces =
            [BlockFace.West, BlockFace.East, BlockFace.North, BlockFace.South, BlockFace.Down, BlockFace.Up];
        foreach (var face in faces)
            this.BlockUpdates.ShapeUpdateAt(position.Offset(face), face.Opposite(), BlockUpdateFlags.Clients, 512);

        await this.BlockNotifications.DrainAsync();
    }

    ValueTask IRedstoneLevel.RestoreBlockSilentlyAsync(Vector position, IBlock block) =>
        this.WriteAsync(position, block, BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);

    IBlock? IBlockTickLevel.GetLoadedBlock(Vector position) => ((IBlockChangeWorld)this).GetLoadedBlock(position);

    bool IBlockTickLevel.IsChunkLoaded(long key)
    {
        NumericsHelper.LongToInts(key, out var chunkX, out var chunkZ);
        return this.GetLoadedChunk(chunkX, chunkZ) is not null;
    }

    bool IBlockTickLevel.IsChunkSimulated(long key)
    {
        NumericsHelper.LongToInts(key, out var chunkX, out var chunkZ);
        return this.IsBlockTicking(new Vector(chunkX * 16 + 8, 0, chunkZ * 16 + 8));
    }

    // A write into an unloaded chunk is dropped: the engine never loads chunks.
    private ValueTask WriteAsync(Vector position, IBlock block, BlockUpdateFlags flags)
    {
        if (this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord()) is null)
            return ValueTask.CompletedTask;

        return this.SetBlockUntrackedAsync(position.X, position.Y, position.Z, block, flags);
    }
}
