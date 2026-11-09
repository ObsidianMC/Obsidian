using Obsidian.API;
using Obsidian.API.Blocks;
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

public sealed class BlockUseSafety
{
    [Fact]
    public async Task DeadOrRespawningClicksAreAcknowledgedWithoutUsingItems()
    {
        await using var context = TestServerFactory.CreateServer($"dead-block-use-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.SetBlockAsync(target, BlocksRegistry.Get("minecraft:obsidian"));
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
            new VectorD(8.5, 64, 10.5));
        player.GameMode = GameMode.Survival;
        var crystals = new ItemStack(ItemsRegistry.EndCrystal, 2);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, crystals);

        foreach (var respawning in new[] { false, true })
        {
            player.Health = respawning ? 20 : 0;
            player.Respawning = respawning;
            ((TestClient)player.Client).ClearPackets();

            var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
            Assert.Null(await packet.UseAsync(context.Server, player));

            Assert.Equal(2, crystals.Count);
            Assert.Empty(world.GetEntitiesInRange((VectorD)target, 8));
            Assert.Single(((TestClient)player.Client).SentPackets.OfType<BlockChangedAckPacket>());
        }
    }

}
