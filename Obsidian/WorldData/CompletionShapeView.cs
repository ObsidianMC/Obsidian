using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Generators.Mojang.Features;

namespace Obsidian.WorldData;

/// <summary>
/// A completing chunk's region as a block's own shape rule reads it from <see cref="IPostProcessBehavior"/>: vanilla's
/// <c>LevelChunk.postProcessGeneration</c> runs <c>updateShape</c> against the live level, so the ticks a rule
/// schedules go to the region's chunk ticks, and the ticks already there read as scheduled.
/// </summary>
/// <remarks>
/// It gives no random: the rules that run through it (speleothems' and kelp's support checks) don't draw.
/// </remarks>
internal sealed class CompletionShapeView(WorldGenRegion region) : IShapeLevel
{
    public IRandomSource Random =>
        throw new NotSupportedException("Completion's support checks don't draw from the level's random.");

    public IBlock GetBlock(Vector position) => region.GetBlock(position);

    public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full) =>
        ShapeViews.IsFaceSturdy(region.GetBlock(position), face, support);

    public IBlockEntity? GetBlockEntity(Vector position) => region.GetBlockEntity(position);

    public int GetRawBrightness(Vector position) => 15;

    public void ScheduleTick(Vector position, IBlock block, int delay, TickPriority priority = TickPriority.Normal) =>
        region.ScheduleBlockTick(position, block, delay);

    public bool HasScheduledTick(Vector position, IBlock block) => region.HasScheduledBlockTick(position, block);
}
