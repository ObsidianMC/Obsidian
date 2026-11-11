using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BedSleepConcurrency
{
    [Fact]
    public async Task SleepDoesNotRestoreBedReplacedDuringSpawnConfirmation()
    {
        foreach (var replaceHead in new[] { true, false })
        {
            await using var context = TestServerFactory.CreateServer($"sleep-race-{Guid.NewGuid():N}");
            var world = context.World;
            WorldBlockEntities.AddEmptyChunk(world);
            var head = new Vector(8, 64, 9);
            var foot = head + Vector.South;
            var bed = BlocksRegistry.RedBed.WithProperty("part", "head").WithProperty("facing", "north");
            world.BlockUpdates.SetBlock(head, bed, BlockUpdateFlags.KnownShape);
            world.BlockUpdates.SetBlock(foot, bed.WithProperty("part", "foot"), BlockUpdateFlags.KnownShape);
            var player = TestServerFactory.CreateConnectedPlayer(
                context.Server, world, "Sleeper", new VectorD(8.5, 64, 8.5));
            var initialPosition = player.Position;
            var client = (TestClient)player.Client;
            var confirmationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumeConfirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.AfterQueue = async packet =>
            {
                if (packet is SystemChatPacket { Message.Translate: "block.minecraft.set_spawn" })
                {
                    confirmationEntered.SetResult();
                    await resumeConfirmation.Task;
                }
            };

            // The packet callback holds sleep at the exact await where a concurrent block change can happen.
            var sleeping = player.StartSleepInBedAsync(
                head, bed, new BedRule("always", "always", false, null)).AsTask();
            try
            {
                Assert.True(confirmationEntered.Task.IsCompletedSuccessfully);
                Assert.False(sleeping.IsCompleted);
                var replacement = replaceHead ? head : foot;
                world.BlockUpdates.SetBlock(replacement, BlocksRegistry.Stone, BlockUpdateFlags.KnownShape);
            }
            finally
            {
                resumeConfirmation.SetResult();
                await sleeping;
                client.AfterQueue = null;
            }

            Assert.True(world.BlockUpdates.GetBlock(replaceHead ? head : foot)!.Is(BlockIds.Stone));
            Assert.Equal("false", world.BlockUpdates.GetBlock(replaceHead ? foot : head)!.GetProperty("occupied"));
            Assert.False(player.Sleeping);
            Assert.Equal(initialPosition, player.Position);
            Assert.Null(player.BedBlockPosition);
        }
    }
}
