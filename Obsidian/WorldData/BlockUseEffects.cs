using Obsidian.API.Blocks;

namespace Obsidian.WorldData;

/// <summary>
/// A use's state-only phase, under the level tick lock. Normal block writes store quietly and retain their
/// notification flags and order. Publishing uses the same post-write path as an ordinary level write, after
/// both inventory locks have been released; nested changes from those notifications then run normally.
/// </summary>
internal sealed class BlockUseEffects : IDisposable
{
    private readonly AbstractLevel level;
    private readonly List<Action> effects = [];

    internal int ThreadId { get; } = Environment.CurrentManagedThreadId;

    internal BlockUseEffects(AbstractLevel level)
    {
        if (level.ActiveBlockUse is not null)
            throw new InvalidOperationException("A block-use state commit cannot be nested.");

        this.level = level;
        level.ActiveBlockUse = this;
    }

    internal bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags)
    {
        var old = this.level.BlockUpdates.GetBlock(position);
        if (old.IsSameState(block) || !this.level.Fluids.SetBlock(position, block))
            return false;

        this.After(() => this.level.AfterBlockSet(position, old, block, flags));
        return true;
    }

    internal void After(Action effect) => this.effects.Add(effect);

    public void Dispose() => this.level.ActiveBlockUse = null;

    internal void Publish()
    {
        if (this.level.ActiveBlockUse is not null)
            throw new InvalidOperationException("Block-use effects cannot run during a state commit.");

        foreach (var effect in this.effects)
            effect();
    }
}

public abstract partial class AbstractLevel
{
    // Accessed only while holding Fluids.TickLock. No state commit awaits or invokes external hooks.
    private BlockUseEffects? activeBlockUse;

    internal BlockUseEffects? ActiveBlockUse
    {
        get
        {
            var active = System.Threading.Volatile.Read(ref this.activeBlockUse);
            return active?.ThreadId == Environment.CurrentManagedThreadId ? active : null;
        }
        set => System.Threading.Volatile.Write(ref this.activeBlockUse, value);
    }
}
