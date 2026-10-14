using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class Cooking
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task MachinesCookAcrossSaveAndAutomationAndAwardExperience()
    {
        await using var context = TestServerFactory.CreateServer($"cooking-{Guid.NewGuid():N}");
        var world = context.World;
        world.RecipeSource = () => context.Server.RecipeRegistry.Current;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        var position = new Vector(2, 70, 2);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Cook", (VectorD)position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);

        foreach (var block in new[] { BlocksRegistry.Furnace, BlocksRegistry.BlastFurnace, BlocksRegistry.Smoker })
        {
            await world.SetBlockAsync(position, block);
            var machine = Assert.IsType<FurnaceBlockEntity>(world.GetResidentBlockEntity(position));
            var input = block == BlocksRegistry.Smoker ? ItemsRegistry.Beef : ItemsRegistry.RawIron;
            var output = block == BlocksRegistry.Smoker ? ItemsRegistry.CookedBeef : ItemsRegistry.IronIngot;
            var time = block == BlocksRegistry.Furnace ? 200 : 100;
            var burn = block == BlocksRegistry.Furnace ? 1600 : 800;
            Assert.Null(world.GetItemAccess(position, BlockFace.Up)!.Insert(new ItemStack(input, 2)));
            Assert.Null(world.GetItemAccess(position, BlockFace.East)!.Insert(new ItemStack(ItemsRegistry.Coal, 1)));
            for (var tick = 0; tick < time / 2; tick++)
                await world.TickFurnaceAsync(machine);

            Assert.Equal(time / 2, machine.CookingSpent);
            Assert.Equal(burn - time / 2 + 1, machine.LitRemaining);
            Assert.Equal("true", world.BlockUpdates.GetBlock(position).GetProperty("lit"));
            var saved = BlockEntityNbt.SaveData(machine);
            Assert.Equal((short)(time / 2), saved.GetShort("cooking_time_spent"));
            Assert.Equal((short)time, saved.GetShort("cooking_total_time"));
            Assert.Equal((short)burn, saved.GetShort("lit_total_time"));
            var restored = Assert.IsType<FurnaceBlockEntity>(BlockEntityNbt.Create(machine.Id, position, saved));
            chunk.SetBlockEntity(position.X, position.Y, position.Z, restored);
            world.AttachBlockEntity(restored);
            for (var tick = time / 2; tick < time; tick++)
                await world.TickFurnaceAsync(restored);

            Assert.True(restored.Inventory[2]?.Is(output));
            Assert.Equal(1, restored.Inventory[0]?.Count);
            Assert.Equal(1, restored.RecipesUsed.Values.Sum());
            var bottom = world.GetItemAccess(position, BlockFace.Down)!;
            Assert.True(bottom.Extract(0, 1)?.Is(output));
            Assert.Equal(1, restored.RecipesUsed.Values.Sum());
            for (var tick = 0; tick < time; tick++)
                await world.TickFurnaceAsync(restored);

            await player.OpenInventoryAsync(restored.Furnace);
            var client = (TestClient)player.Client;
            Assert.Equal(4, client.SentPackets.OfType<ContainerSetDataPacket>()
                .Count(packet => packet.ContainerId == player.CurrentContainerId));
            await TakeResult(player);
            Assert.True(player.CarriedItem?.Is(output));
            Assert.Empty(restored.RecipesUsed);
            Assert.Contains(
                world.Regions.Values.SelectMany(region => region.Entities.Values), entity => entity is ExperienceOrb);
            player.CarriedItem = null;
            await player.CloseContainerAsync();
            await world.SetBlockAsync(position, BlocksRegistry.Air);
        }

        // Burn durations reference FuelValues, including its rounding and excluded nether wood.
        Assert.Equal(4001, FurnaceFuels.BurnTime(new ItemStack(ItemsRegistry.DriedKelpBlock, 1)));
        Assert.Equal(67, FurnaceFuels.BurnTime(new ItemStack(ItemsRegistry.WhiteCarpet, 1)));
        Assert.Equal(0, FurnaceFuels.BurnTime(new ItemStack(ItemsRegistry.CrimsonPlanks, 1)));
        await world.SetBlockAsync(position, BlocksRegistry.Furnace);
        var sponge = Assert.IsType<FurnaceBlockEntity>(world.GetResidentBlockEntity(position));
        sponge.Inventory[0] = new ItemStack(ItemsRegistry.WetSponge, 1);
        sponge.Inventory[1] = new ItemStack(ItemsRegistry.LavaBucket, 1);
        for (var tick = 0; tick < 200; tick++)
            await world.TickFurnaceAsync(sponge);

        Assert.Equal(20000, sponge.LitTotal);
        Assert.True(sponge.Inventory[1]?.Is(ItemsRegistry.WaterBucket));
        Assert.True(sponge.Inventory[2]?.Is(ItemsRegistry.Sponge));

        await world.SetBlockAsync(position, BlocksRegistry.Campfire);
        player.Inventory[36] = new ItemStack(ItemsRegistry.Beef, 2);
        Assert.True(await world.UseCampfireAsync(player, position, 36));
        Assert.Equal(1, player.Inventory[36]?.Count);
        var campfire = Assert.IsType<CampfireBlockEntity>(world.GetResidentBlockEntity(position));
        for (var tick = 0; tick < 100; tick++)
            await world.TickCampfireAsync(campfire);

        var campfireSave = BlockEntityNbt.SaveData(campfire);
        Assert.Equal(100, FirstTime(campfireSave));
        var reloaded = Assert.IsType<CampfireBlockEntity>(
            BlockEntityNbt.Create("minecraft:campfire", position, campfireSave));
        chunk.SetBlockEntity(position.X, position.Y, position.Z, reloaded);
        world.AttachBlockEntity(reloaded);
        await world.SetBlockAsync(position, BlocksRegistry.Campfire.WithProperty("lit", false));
        await world.TickCampfireAsync(reloaded);
        Assert.Equal(98, FirstTime(BlockEntityNbt.SaveData(reloaded)));
        await world.SetBlockAsync(position, BlocksRegistry.Campfire);
        for (var tick = 98; tick < 600; tick++)
            await world.TickCampfireAsync(reloaded);

        Assert.Null(reloaded.Inventory[0]);
        Assert.Contains(
            world.Regions.Values.SelectMany(region => region.Entities.Values),
            entity => entity is ItemEntity item && item.Item.Is(ItemsRegistry.CookedBeef));
        Assert.Null(world.GetItemAccess(position, BlockFace.Up));
    }

    private static int FirstTime(NbtCompound tag)
    {
        Assert.True(tag.TryGetTag<NbtArray<int>>("CookingTimes", out var times));
        return times[0];
    }

    private static async Task TakeResult(Obsidian.Entities.Player player)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt(player.CurrentContainerId);
        writer.WriteVarInt(0);
        writer.WriteShort(2);
        writer.WriteByte(0);
        writer.WriteVarInt((int)ClickType.Pickup);
        writer.WriteVarInt(0);
        writer.WriteBoolean(false);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new ContainerClickPacket();
        packet.Populate(reader);
        await packet.HandleAsync(player.Server, player);
    }
}

