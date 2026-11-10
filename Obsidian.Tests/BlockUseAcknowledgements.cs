using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BlockUseAcknowledgements
{
    [Fact]
    public async Task ConnectionPassAcknowledgesRefusalsWhilePausedAndOutsideLevelMembership()
    {
        await using var context = TestServerFactory.CreateServer($"connection-acks-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
        world.BlockUpdates.SetBlock(target + Vector.Up, BlocksRegistry.Stone);
        var paused = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Paused",
            new VectorD(8.5, 64, 10.5));
        var arriving = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Arriving",
            new VectorD(8.5, 64, 10.5));
        Assert.True(world.TryAddPlayer(paused));
        Assert.True(world.TryAddPlayer(arriving));
        Assert.True(world.TryRemovePlayer(arriving));
        arriving.Respawning = true;
        Assert.True(await context.Server.PauseAsync());

        foreach (var player in new[] { paused, arriving })
        {
            player.GameMode = GameMode.Survival;
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Stone, 1));
            var client = (TestClient)player.Client;
            client.ClearPackets();
            await BlockUpdates.UseOn(target, BlockFace.Up, sequence: 11).HandleAsync(context.Server, player);
            await BlockUpdates.UseOn(target, BlockFace.Up, sequence: 12).HandleAsync(context.Server, player);

            Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);
            Assert.Equal(4, client.SentPackets.OfType<BlockUpdatePacket>().Count());
            Assert.Contains(client.SentPackets, packet => packet is ContainerSetSlotPacket);
        }

        // Drive the same periodic connection pass as the live server, without a timer or a world tick.
        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        foreach (var player in new[] { paused, arriving })
        {
            var client = (TestClient)player.Client;
            Assert.Equal(12, Assert.Single(client.SentPackets.OfType<BlockChangedAckPacket>()).SequenceID);
            Assert.IsType<BlockChangedAckPacket>(client.SentPackets.Last());
            Assert.Equal(1, player.GetHeldItem()?.Count);
        }

        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        Assert.Single(((TestClient)paused.Client).SentPackets.OfType<BlockChangedAckPacket>());
        Assert.Single(((TestClient)arriving.Client).SentPackets.OfType<BlockChangedAckPacket>());
        Assert.True(context.Server.Paused);
        Assert.False(world.Players.ContainsKey(arriving.Uuid));
    }
}
