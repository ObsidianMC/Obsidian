using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// The live level as <see cref="Features.Tree.ShapeUpdater"/> reads it. Reads don't load chunks, and an unloaded position
/// reads as void air; fluid ticks and light go through the level. Shape updates return states, so writes aren't
/// supported.
/// </summary>
/// <remarks>
/// It schedules no block ticks and reads none as scheduled: the rules that schedule them are live owners
/// (<see cref="ShapeReach"/>), which the level runs before this pass.
/// </remarks>
internal sealed class LiveShapeView(IBlockChangeWorld world) : IWorldGenLevel, IShapeLevel
{
    public long Seed => throw new NotSupportedException();

    public int MinY => world.MinY;

    public int Height => world.Height;

    public int SeaLevel => throw new NotSupportedException();

    public IRandomSource Random => throw new NotSupportedException();

    public IBlock GetBlock(Vector position) => world.GetLoadedBlock(position) ?? BlocksRegistry.VoidAir;

    public bool SetBlock(Vector position, IBlock block) => throw new NotSupportedException("Shape updates return their state; the pipeline commits it.");

    public bool EnsureCanWrite(Vector position) => false;

    public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

    public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

    public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

    public IBlockEntity? GetBlockEntity(Vector position) => throw new NotSupportedException();

    public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

    public void MarkForPostProcessing(Vector position) => throw new NotSupportedException();

    public void ScheduleFluidTick(Vector position) => world.ScheduleFluidTick(position);

    public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full) =>
        Features.Tree.ShapeViews.IsFaceSturdy(this.GetBlock(position), face, support);

    public int GetRawBrightness(Vector position) => world.GetRawBrightness(position);

    public void ScheduleTick(Vector position, IBlock block, int delay, TickPriority priority = TickPriority.Normal)
    {
    }

    public bool HasScheduledTick(Vector position, IBlock block) => false;
}
