using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class EntityContactSafety
{
    [Fact]
    public async Task CellsWithoutHooksAllocateNothing()
    {
        await using var level = TestWorldFactory.Create("empty-contact-allocation");
        var blocks = new RedstoneTestWorld();
        blocks.Load(0, 15, 62, 67, 0, 15);
        var entity = new ItemEntity
        {
            Level = level,
            Position = new VectorD(8.5, 64, 8.5),
            MovementFlags = MovementFlags.OnGround
        };
        var contacts = new EntityContactPass(blocks);
        for (var index = 0; index < 20_000; index++)
            contacts.TouchAsync(entity).GetAwaiter().GetResult();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100_000; index++)
            contacts.TouchAsync(entity).GetAwaiter().GetResult();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
