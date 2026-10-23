using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>ComposterBlock</c> overrides of <c>tick</c> and <c>onPlace</c>: a full composter (level 7) becomes
/// ready (8) after 20 ticks, with its sound; a block that just became full schedules that tick.
/// </summary>
internal sealed class ComposterBlockBehavior : IScheduledTickBehavior, IOnPlaceBehavior
{
    /// <summary>Vanilla's <c>ComposterBlock.tick</c>.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (Composters.LevelOf(block) != Composters.Full)
            return;

        level.SetBlock(position, block.WithProperty("level", Composters.Ready.ToString()));
        if (level.Level is AbstractLevel abstractLevel)
            Composters.PlaySound(abstractLevel, position, Obsidian.API.SoundId.Block.ComposterReady);
    }

    /// <summary>Vanilla's <c>ComposterBlock.onPlace</c>: the level reached full schedules the tick to ready it.</summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        if (Composters.LevelOf(state) == Composters.Full && Composters.LevelOf(oldState) != Composters.Full)
            level.ScheduleTick(position, state, Composters.ReadyDelay);
    }
}
