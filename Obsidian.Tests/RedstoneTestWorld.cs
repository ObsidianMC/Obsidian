using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.Tests;

/// <summary>
/// A level made of real <see cref="Chunk"/>s, with the live pipeline and the redstone engine installed the way
/// <c>AbstractLevel</c> installs them. Only the chunks it loads exist; reads of any other position see no block, like an unloaded chunk.
/// </summary>
/// <summary>An arrow that enters <see cref="Block"/> at <see cref="Location"/> through <see cref="Face"/>.</summary>
public sealed record ArrowHit(Vector Block, VectorD Location, BlockFace Face);

internal sealed class RedstoneTestWorld : IPressurePlateWorld, IRedstoneLevel, IBlockTickLevel
{
    private readonly Dictionary<long, Chunk> chunks = [];
    private readonly Queue<Func<ValueTask>> simulation = new();

    public RedstoneTestWorld(Action<Vector>? playNote = null)
    {
        this.Neighbors = new NeighborBehaviors();
        this.Redstone = new RedstoneBehaviors();
        this.BlockTicks = new LevelBlockTicks(this, new System.Threading.Lock());
        var install = RedstoneRegistration.Install(this, this.BlockTicks, this.Neighbors, this.Redstone, playNote);
        this.Engine = install.Engine;
        this.Tripwires = install.Tripwires;
        this.Targets = install.Targets;
        this.Pipeline = new BlockNotificationPipeline(this, this.Neighbors);
    }

    /// <summary>The level's block ticks, run by <see cref="Advance"/>.</summary>
    public LevelBlockTicks BlockTicks { get; }

    /// <summary>The game and day clocks, and the sky light (open sky by default).</summary>
    public long DayTime { get; set; }

    public int SkyLight { get; set; } = 15;

    public bool TntExplodes { get; set; } = true;

    public List<Vector> PrimedTnt { get; } = [];

    /// <summary>Every block tick scheduled through the world, as (position, block, delay), in scheduling order.</summary>
    public List<(Vector Position, string Block, int Delay)> ScheduledBlockTicks { get; } = [];

    /// <summary>
    /// Saves and restarts the level's block ticks, as a save and server restart does: every chunk stops ticking (its ticks are saved
    /// relative to now, like <c>ChunkBlockTicks.Pack</c>) and starts again, so ticks keep their remaining delays.
    /// </summary>
    public void Restart()
    {
        foreach (var chunk in this.chunks.Values)
            chunk.BlockTicks.StopTicking();

        foreach (var chunk in this.chunks.Values)
            this.BlockTicks.Track(chunk);
    }

    /// <summary>
    /// Runs one game tick the way the level does: block ticks first, then the deferred simulation work, then the notifications
    /// they queued.
    /// </summary>
    /// <param name="contacts">
    /// The cells that entities touch during this tick (the level's entity pass). They are applied after the block ticks, as the
    /// game step does, so a tripwire scheduled by a contact counts its delay from this tick.
    /// </param>
    /// <param name="arrowHits">Arrows that enter a block during this tick, applied after <paramref name="contacts"/>.</param>
    public async ValueTask Advance(IReadOnlyList<Vector>? contacts = null, IReadOnlyList<ArrowHit>? arrowHits = null)
    {
        this.BlockTicks.Tick();
        if (contacts is not null)
            await this.Tripwires.ApplyContactsAsync(contacts);

        foreach (var hit in arrowHits ?? [])
            await this.Targets.HitAsync(hit.Block, hit.Location, hit.Face, TargetBlock.ArrowPulseTicks);

        while (this.simulation.Count > 0)
            await this.simulation.Dequeue()();

        // Vanilla ticks block entities after the block ticks; moving pistons advance here.
        foreach (var chunk in this.chunks.Values.ToList())
        {
            foreach (var moving in chunk.BlockEntities.Values.OfType<Obsidian.WorldData.Pistons.MovingPistonBlockEntity>().ToList())
                await moving.TickAsync();
        }

        await this.Pipeline.DrainAsync();
    }

    public NeighborBehaviors Neighbors { get; }

    public RedstoneBehaviors Redstone { get; }

    public RedstoneEngine Engine { get; }

    public TripWire Tripwires { get; }

    /// <summary>Target blocks, the projectile-hit path of the level.</summary>
    public TargetBlock Targets { get; }

    public BlockNotificationPipeline Pipeline { get; }

    public int MinY => -64;

    public int Height => 384;

    public List<Vector> FluidTicks { get; } = [];

    /// <summary>
    /// Loads every chunk covering the box, with air, plus <paramref name="ringChunks"/> chunks around it. Chunks outside stay
    /// unloaded. A chunk's block ticks run only while its eight neighbors are loaded, so a scenario that ticks needs a ring of 1.
    /// </summary>
    public void Load(int minX, int maxX, int minY, int maxY, int minZ, int maxZ, int ringChunks = 0)
    {
        for (var cx = (minX >> 4) - ringChunks; cx <= (maxX >> 4) + ringChunks; cx++)
        {
            for (var cz = (minZ >> 4) - ringChunks; cz <= (maxZ >> 4) + ringChunks; cz++)
            {
                var key = NumericsHelper.IntsToLong(cx, cz);
                if (this.chunks.ContainsKey(key))
                    continue;

                var chunk = new Chunk(cx, cz);
                chunk.SetChunkStatus(ChunkGenStage.full);
                this.chunks[key] = chunk;
                this.BlockTicks.Track(chunk);
            }
        }
    }

    public IBlock? GetLoadedBlock(Vector position)
    {
        if (position.Y < this.MinY || position.Y >= this.MinY + this.Height)
            return BlocksRegistry.VoidAir;

        return this.chunks.TryGetValue(NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4), out var chunk)
            ? chunk.GetBlock(position.X, position.Y, position.Z)
            : null;
    }

    /// <summary>
    /// Commits a block without queueing its notifications, like the level does before it queues them.
    /// </summary>
    public BlockChange Commit(Vector position, IBlock block)
    {
        var old = this.GetLoadedBlock(position) ?? throw new InvalidOperationException($"{position} isn't loaded.");
        var chunk = this.chunks[NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4)];
        chunk.SetBlock(position.X, position.Y, position.Z, block);

        // Like AbstractLevel.UpdateBlockEntity: a block entity stays only while the block still has its type.
        if (chunk.GetBlockEntity(position.X, position.Y, position.Z) is BlockEntity existing
            && !string.Equals(existing.Id, block.BlockEntityType(), StringComparison.OrdinalIgnoreCase))
        {
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);
        }

        return new BlockChange(position, old, block);
    }

    /// <summary>
    /// A complete block placement: commits the block, queues its notifications and drains them, as <c>SetBlockAsync</c> does.
    /// </summary>
    public async ValueTask Place(Vector position, IBlock block)
    {
        this.Pipeline.Enqueue(this.Commit(position, block));
        await this.Pipeline.DrainAsync();
    }

    int IBlockChangeWorld.GetRawBrightness(Vector position) => this.SkyLight;

    ValueTask IBlockChangeWorld.SetBlockAsync(Vector position, IBlock block) => this.Place(position, block);

    // Signal tests place pressure plates without terrain fixtures; integration tests cover their support removal.
    void IBlockChangeWorld.DropAndRemove(Vector position, IBlock block, bool playEffect)
    {
        if (block.BlockClass() is not ("PressurePlateBlock" or "WeightedPressurePlateBlock"))
            this.Commit(position, BlocksRegistry.Air);
    }

    public void ScheduleFluidTick(Vector position) => this.FluidTicks.Add(position);

    async ValueTask IBlockChangeWorld.NotifyNeighborsAsync(Vector position)
    {
        this.Pipeline.EnqueueNeighbors(position);
        await this.Pipeline.DrainAsync();
    }

    /// <summary>Plate release ticks scheduled through the world, as (position, block, delay).</summary>
    public List<(Vector Position, string Block, int Delay)> ScheduledPlateTicks { get; } = [];

    void IPressurePlateWorld.SchedulePlateTick(Vector position, IBlock block, int delay) =>
        this.ScheduledPlateTicks.Add((position, block.UnlocalizedName, delay));

    public long GameTime => this.BlockTicks.GameTime;

    long IRedstoneLevel.GameTime => this.GameTime;

    long IRedstoneLevel.DayTime => this.DayTime;

    int IRedstoneLevel.GetSkyLight(Vector position) => this.GetLoadedBlock(position) is null ? 0 : this.SkyLight;

    /// <summary>Crafts requested by crafters, in order (the real level crafts on its entity path).</summary>
    public List<Vector> CraftRequests { get; } = [];

    void IRedstoneLevel.SetCrafterTriggered(Vector position, bool triggered)
    {
    }

    void IRedstoneLevel.CraftCrafter(Vector position) => this.CraftRequests.Add(position);

    /// <summary>Dispenser and dropper fires requested, in order (the real level fires on its entity path).</summary>
    public List<(Vector Position, bool Dropper)> DispenserFires { get; } = [];

    void IRedstoneLevel.FireDispenser(Vector position, IBlock state, bool dropper) => this.DispenserFires.Add((position, dropper));

    /// <summary>The level events broadcast, by type and position.</summary>
    public List<(int Type, Vector Position)> LevelEvents { get; } = [];

    void IRedstoneLevel.BroadcastLevelEvent(int type, Vector position, int data) => this.LevelEvents.Add((type, position));

    /// <summary>The sounds played, with the player each one excludes.</summary>
    public List<(Vector Position, string Sound, IPlayer? Except)> Sounds { get; } = [];

    void IBlockChangeWorld.PlaySound(Vector position, string sound, float volume, float pitch, IPlayer? except) =>
        this.Sounds.Add((position, sound, except));

    /// <summary>The entities in the test level; <see cref="IRedstoneLevel.EntitiesIn"/> returns those whose boxes overlap.</summary>
    public List<IEntity> Entities { get; } = [];

    ValueTask IRedstoneLevel.NotifyNeighborAsync(Vector target, Vector source)
    {
        this.Pipeline.EnqueueNeighbor(target, source);
        return this.Pipeline.DrainAsync();
    }

    ValueTask IRedstoneLevel.NotifyComparatorsAroundAsync(Vector position)
    {
        foreach (var direction in new[] { BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East })
        {
            var beside = position.Offset(direction);
            if (this.GetLoadedBlock(beside) is not IBlock state)
                continue;

            if (state.Is(BlockIds.Comparator))
                this.Pipeline.EnqueueNeighbor(beside, position);
            else if (state.IsRedstoneConductor() && this.GetLoadedBlock(beside.Offset(direction)) is IBlock behind && behind.Is(BlockIds.Comparator))
                this.Pipeline.EnqueueNeighbor(beside.Offset(direction), position);
        }

        return this.Pipeline.DrainAsync();
    }

    IBlockEntity? IRedstoneLevel.GetBlockEntityAt(Vector position) =>
        this.chunks.TryGetValue(NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4), out var chunk)
            ? chunk.GetBlockEntity(position.X, position.Y, position.Z)
            : null;

    void IRedstoneLevel.SetBlockEntityAt(Vector position, IBlockEntity entity)
    {
        if (this.chunks.TryGetValue(NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4), out var chunk) && entity is BlockEntity placed)
        {
            placed.BlockPosition = position;
            chunk.SetBlockEntity(position.X, position.Y, position.Z, placed);
        }
    }

    IEnumerable<IEntity> IRedstoneLevel.EntitiesIn(VectorD min, VectorD max) => this.Entities.Where(entity =>
    {
        var box = entity.BoundingBox;
        return box.Min.X < max.X && box.Max.X > min.X
            && box.Min.Y < max.Y && box.Max.Y > min.Y
            && box.Min.Z < max.Z && box.Max.Z > min.Z;
    });

    bool IRedstoneLevel.HasAnalogOutput(IBlock block) => false;

    int IRedstoneLevel.GetAnalogOutput(Vector position, IBlock block, BlockFace direction) => 0;

    void IRedstoneLevel.ScheduleBlockTick(Vector position, IBlock block, int delay, Obsidian.API.Blocks.TickPriority priority)
    {
        this.ScheduledBlockTicks.Add((position, block.UnlocalizedName, delay));
        this.BlockTicks.Schedule(position, block.UnlocalizedName, delay, (int)priority);
    }

    bool IRedstoneLevel.IsBlockTickScheduled(Vector position, string block) => this.BlockTicks.IsScheduled(position, block);

    void IRedstoneLevel.EnqueueSimulation(Func<ValueTask> action) => this.simulation.Enqueue(action);

    bool IRedstoneLevel.TntExplodes => this.TntExplodes;

    void IRedstoneLevel.PrimeTnt(Vector position) => this.PrimedTnt.Add(position);

    async ValueTask IRedstoneLevel.WriteAsync(Vector position, IBlock block, bool notifyNeighbors)
    {
        if (notifyNeighbors)
            await this.Place(position, block);
        else
            this.Commit(position, block);
    }

    bool IBlockTickLevel.IsChunkLoaded(long key) => this.chunks.ContainsKey(key);
}
