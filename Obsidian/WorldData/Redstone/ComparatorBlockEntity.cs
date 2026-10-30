using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A comparator's block entity: the strength it outputs (see <see cref="DiodeKind.KeepsStrength"/>), saved as
/// vanilla's <c>OutputSignal</c>.
/// </summary>
internal sealed class ComparatorBlockEntity : BlockEntity
{
    private int outputSignal;

    /// <summary>
    /// The power the comparator gives while powered, set under the level's tick lock.
    /// </summary>
    public int OutputSignal
    {
        get => Volatile.Read(ref this.outputSignal);
        set => Volatile.Write(ref this.outputSignal, value);
    }

    // It's saved as it is whenever its chunk is.
    internal override bool SavesOnAnyThread => true;

    protected internal override void Load(NbtCompound tag) =>
        this.OutputSignal = tag.TryGetNumber<int>("OutputSignal", out var signal) ? signal : 0;

    protected internal override void Save(NbtCompound tag) =>
        tag.Add(new NbtTag<int>("OutputSignal", this.OutputSignal));
}
