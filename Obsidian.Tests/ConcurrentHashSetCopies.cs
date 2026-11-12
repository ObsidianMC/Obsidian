using System.Linq;
using System.Threading.Tasks;
using Obsidian.API.Utilities.Concurrency;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Sets such as a player's loaded chunks are copied while other threads add to them. LINQ's ToArray and List.AddRange
/// count the set, then copy it, and threw when an add landed in between.
/// </summary>
public sealed class ConcurrentHashSetCopies
{
    [Fact]
    public async Task CopiesWhileAnotherThreadAdds()
    {
        var set = new ConcurrentHashSet<long>();
        var adding = Task.Run(() =>
        {
            for (long item = 0; item < 200_000; item++)
                set.Add(item);
        });

        while (!adding.IsCompleted)
        {
            var items = set.ToArray();
            Assert.Equal(items.Length, items.Distinct().Count());
        }

        await adding;
        Assert.Equal(200_000, set.ToArray().Length);
    }
}
