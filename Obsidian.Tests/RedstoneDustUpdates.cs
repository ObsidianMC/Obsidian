using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Redstone dust a player places and breaks reaches the client in the state the server stores: the dust's shape
/// updates to the dust diagonal to it (vanilla <c>updateIndirectNeighbourShapes</c>) are block changes like any other.
/// </summary>
public sealed class RedstoneDustUpdates
{
    private const int Ground = BlockUpdates.Ground;

    // A one-block step on flat ground, with dust beside it and dust on top of it.
    private static readonly Vector Step = new(8, Ground + 1, 8);
    private static readonly Vector Lower = Step + Vector.East;
    private static readonly Vector Upper = Step + Vector.Up;

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DustPlacedOnAStepClimbsToTheDustBelowAndLetsGoWhenBroken()
    {
        await using var context = TestServerFactory.CreateServer($"dust-step-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        await world.SetBlockAsync(Step, BlocksRegistry.Stone);
        var (player, client) = Join(context);

        // The dust beside the step goes down first, as a cross: nothing is on the step to climb to yet.
        await PlaceDustAsync(context, player, Lower + Vector.Down, sequence: 1);
        world.FlushBlockChanges();

        AssertDust(world, client, Lower, north: "side", east: "side", south: "side", west: "side");

        // The dust on top of the step runs east to its edge, and the dust below climbs the step's side to it.
        client.ClearPackets();
        await PlaceDustAsync(context, player, Step, sequence: 2);
        world.FlushBlockChanges();

        AssertDust(world, client, Upper, north: "none", east: "side", south: "none", west: "side");
        AssertDust(world, client, Lower, north: "none", east: "side", south: "none", west: "up");

        // Breaking the dust on the step turns the dust below back into a cross.
        client.ClearPackets();
        await StartBreaking(Upper, BlockFace.Up, sequence: 3).HandleAsync(context.Server, player);
        world.FlushBlockChanges();

        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(Upper).IsAir);
        AssertDust(world, client, Lower, north: "side", east: "side", south: "side", west: "side");
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DustPlacedBelowAStepReshapesTheDustOnTopOfIt()
    {
        await using var context = TestServerFactory.CreateServer($"dust-step-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        await world.SetBlockAsync(Step, BlocksRegistry.Stone);
        var (player, client) = Join(context);

        // The dust on top of the step goes down first, as a cross.
        await PlaceDustAsync(context, player, Step, sequence: 1);
        world.FlushBlockChanges();

        AssertDust(world, client, Upper, north: "side", east: "side", south: "side", west: "side");

        // The dust beside the step climbs it, and the dust on top, reached through the dust above the step's side,
        // becomes a line running to its edge.
        client.ClearPackets();
        await PlaceDustAsync(context, player, Lower + Vector.Down, sequence: 2);
        world.FlushBlockChanges();

        AssertDust(world, client, Lower, north: "none", east: "side", south: "none", west: "up");
        AssertDust(world, client, Upper, north: "none", east: "side", south: "none", west: "side");
    }

    // The dust at position is stored with these sides, and the last state the client was sent for it is the stored
    // one.
    private static void AssertDust(
        WorldData.World world,
        TestClient client,
        Vector position,
        string north,
        string east,
        string south,
        string west)
    {
        var stored = world.GetLoadedChunk(0, 0)!.GetBlock(position);
        Assert.True(stored.Is(BlockIds.RedstoneWire), $"{position} holds {stored.UnlocalizedName}, not dust.");

        var sides = (stored.GetProperty("north"), stored.GetProperty("east"), stored.GetProperty("south"),
            stored.GetProperty("west"));
        Assert.Equal((north, east, south, west), sides);

        var sent = BlockUpdates.SentBlocks(client)
            .Where(block => block.Position == position)
            .Select(block => block.StateId)
            .ToArray();
        Assert.NotEmpty(sent);
        Assert.Equal(stored.GetHashCode(), sent[^1]);
    }

    private static async Task PlaceDustAsync(RealServerContext context, Entities.Player player, Vector clicked,
        int sequence)
    {
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Get("minecraft:redstone"), 1));
        await BlockUpdates.UseOn(clicked, BlockFace.Up, sequence: sequence).HandleAsync(context.Server, player);
    }

    private static (Entities.Player Player, TestClient Client) Join(RealServerContext context)
    {
        var position = new VectorD(10.5, Ground + 1, 10.5);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Redstoner", position);
        player.GameMode = GameMode.Creative;
        Assert.True(context.World.TryAddPlayer(player));
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));

        var client = (TestClient)player.Client;
        client.ClearPackets();
        return (player, client);
    }

    private static PlayerActionPacket StartBreaking(Vector position, BlockFace face, int sequence)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt((int)PlayerActionStatus.StartedDigging);
        writer.WritePosition(position);
        writer.WriteByte((byte)face);
        writer.WriteVarInt(sequence);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new PlayerActionPacket();
        packet.Populate(reader);

        return packet;
    }
}
