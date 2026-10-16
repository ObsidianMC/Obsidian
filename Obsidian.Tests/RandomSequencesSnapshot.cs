using Obsidian.API.Loot;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A loot query draws from a snapshot of the level's random sequences, so it can't change what a real break draws next.
/// </summary>
public class RandomSequencesSnapshot
{
    [Fact]
    public void DrawsFromASnapshotDoNotAdvanceTheLiveSequence()
    {
        const string id = "minecraft:blocks/test";

        var expectedFirst = new RandomSequences(1234).Get(id).NextLong();

        var live = new RandomSequences(1234);
        var snapshot = live.Snapshot();
        snapshot.Get(id).NextLong();
        snapshot.Get(id).NextLong();

        Assert.Equal(expectedFirst, live.Get(id).NextLong());
    }

    [Fact]
    public void SnapshotStartsAtTheLiveSequencesCurrentState()
    {
        const string id = "minecraft:blocks/test";

        var live = new RandomSequences(99);
        live.Get(id).NextLong();
        var expectedSecond = Advance(new RandomSequences(99), id);

        var snapshot = live.Snapshot();

        Assert.Equal(expectedSecond, snapshot.Get(id).NextLong());
    }

    private static long Advance(RandomSequences sequences, string id)
    {
        sequences.Get(id).NextLong();
        return sequences.Get(id).NextLong();
    }
}
