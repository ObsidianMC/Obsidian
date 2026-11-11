using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// What vanilla's captures can't check about command blocks: who may edit them and see their data, that their
/// commands can't do what only operators above the game master level may, that their data survives a save, and that
/// a respawned player's client is told its permission level again.
/// </summary>
public sealed class CommandBlockSafety
{
    private static readonly Vector target = new(8, 64, 8);

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    // Vanilla's advMode.notAllowed.
    private const string NotAllowed = "Must be an opped player in creative mode";

    private sealed record Editor(string Name, int Level, GameMode Mode);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task OnlyCreativeGameMastersEditCommandBlocks()
    {
        await using var context = await StartAsync();
        var world = context.World;
        world.BlockUpdates.SetBlock(target, BlocksRegistry.CommandBlock.WithProperty("facing", "up"), Quiet);

        Editor[] refused =
        [
            new("not an operator", 0, GameMode.Creative),
            new("below game master", 1, GameMode.Creative),
            new("not in creative", 4, GameMode.Survival)
        ];

        foreach (var editor in refused)
        {
            var player = await JoinAsync(context, editor.Name, editor.Level, editor.Mode);
            await EditPacket(target, "say edited").HandleAsync(context.Server, player);

            Assert.True(world.BlockUpdates.GetBlock(target).Is(BlockIds.CommandBlock), editor.Name);
            Assert.Equal(string.Empty, Entity(world).Command.Command);
            Assert.Equal([NotAllowed], Chat(player));
        }

        // A game master in creative mode turns it into an always active repeating block.
        var master = await JoinAsync(context, "game master", 2, GameMode.Creative);
        await EditPacket(target, "say edited").HandleAsync(context.Server, master);

        Assert.True(world.BlockUpdates.GetBlock(target).Is(BlockIds.RepeatingCommandBlock));
        Assert.Equal("say edited", Entity(world).Command.Command);
        Assert.True(Entity(world).Automatic);
        Assert.Equal(["Command set: say edited"], Chat(master));

        // Naming a block that isn't a command block changes nothing.
        var elsewhere = target + new Vector(1, 0, 0);
        await EditPacket(elsewhere, "say elsewhere").HandleAsync(context.Server, master);
        Assert.True(world.BlockUpdates.GetBlock(elsewhere).IsAir);
        Assert.Empty(Chat(master));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task OnlyEditorsAreSentACommandBlocksData()
    {
        await using var context = await StartAsync();
        var world = context.World;
        world.BlockUpdates.SetBlock(target, BlocksRegistry.CommandBlock.WithProperty("facing", "up"), Quiet);
        Entity(world).Command.SetCommand("say secret");

        // A player who may not edit it uses it as any block: nothing is sent.
        var visitor = await JoinAsync(context, "visitor", 0, GameMode.Creative);
        await BlockUpdates.UseOn(target, BlockFace.North).HandleAsync(context.Server, visitor);
        Assert.Empty(((TestClient)visitor.Client).SentPackets.OfType<BlockEntityDataPacket>());

        // An editor's client opens the editor and is sent the block's data for it.
        var editor = await JoinAsync(context, "editor", 2, GameMode.Creative);
        await BlockUpdates.UseOn(target, BlockFace.North).HandleAsync(context.Server, editor);

        var data = Assert.Single(((TestClient)editor.Client).SentPackets.OfType<BlockEntityDataPacket>());
        Assert.Equal(target, data.Position);
        Assert.Equal(BlockEntityTypesRegistry.NetworkId(CommandBlockEntity.TypeId), data.Type);
        Assert.Equal("say secret", data.NBTData.GetString("Command"));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task CommandBlocksRunAtTheGameMasterLevel()
    {
        await using var context = await StartAsync();
        var world = context.World;
        ((CommandHandler)context.Server.CommandHandler).RegisterCommands();
        var victim = await JoinAsync(context, "Victim", 0, GameMode.Survival);

        // /say needs the game master level, which command blocks have; /kick needs more.
        var allowed = target;
        var refused = target + new Vector(0, 0, 2);
        PlaceImpulse(world, allowed, "say hello");
        PlaceImpulse(world, refused, "kick Victim");

        await world.SetBlockAsync(allowed + new Vector(-1, 0, 0), BlocksRegistry.RedstoneBlock, doBlockUpdate: true);
        await world.SetBlockAsync(refused + new Vector(-1, 0, 0), BlocksRegistry.RedstoneBlock, doBlockUpdate: true);
        await world.DoWorldTickAsync();

        Assert.Equal(1, Entity(world, allowed).Command.SuccessCount);
        Assert.Equal(0, Entity(world, refused).Command.SuccessCount);
        Assert.True(context.Server.IsPlayerOnline(victim.Uuid));
    }

    private sealed record SavedRow(string Name, string Data, Action<CommandBlockEntity> Check);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task CommandBlockDataSurvivesASave()
    {
        SavedRow[] rows =
        [
            new("as vanilla saves a repeating block that ran",
                "{Command:\"setblock ~ ~2 ~ minecraft:stone\",CustomName:\"Builder\",LastExecution:1234L,"
                + "LastOutput:{extra:[{color:\"red\",translate:\"commands.setblock.failed\"}],text:\"[12:34:56] \"},"
                + "SuccessCount:3,TrackOutput:1b,UpdateLastExecution:1b,auto:1b,components:{},conditionMet:1b,"
                + "id:\"minecraft:command_block\",powered:0b}",
                entity =>
                {
                    Assert.Equal(3, entity.Command.SuccessCount);
                    Assert.Equal("Builder", entity.Command.Name);
                    Assert.Equal(1234, entity.Command.LastExecution);
                    Assert.True(entity.Automatic);
                    Assert.True(entity.ConditionMet);
                    Assert.False(entity.Powered);
                }),
            new("without tracked output or recorded executions",
                "{Command:\"say hi\",SuccessCount:0,TrackOutput:0b,UpdateLastExecution:0b,auto:0b,components:{},"
                + "conditionMet:0b,id:\"minecraft:command_block\",powered:1b}",
                entity =>
                {
                    Assert.Null(entity.Command.LastOutput);
                    Assert.Equal(-1, entity.Command.LastExecution);
                    Assert.True(entity.Powered);
                })
        ];

        foreach (var row in rows)
        {
            // Loaded as a chunk loads it, saved as a chunk saves it, then loaded again from what was saved.
            var vanilla = SnbtReader.ReadCompound(row.Data);
            var loaded = Assert.IsType<CommandBlockEntity>(BlockEntityNbt.Load(vanilla, CommandBlockEntity.TypeId,
                target));
            row.Check(loaded);

            var saved = Without(BlockEntityNbt.Save(loaded)!, "x", "y", "z", "keepPacked");
            Assert.Equal(Snbt.Write(vanilla), Snbt.Write(saved));

            var reloaded = BlockEntityNbt.Load(saved, CommandBlockEntity.TypeId, target);
            row.Check(Assert.IsType<CommandBlockEntity>(reloaded));
        }

        // A command block minecart saves its command the same way, without a name of its own.
        await using var context = await StartAsync();
        var cart = new CommandBlockMinecart { Level = context.World };
        cart.Stored.Load(SnbtReader.ReadCompound(
            "{Command:\"say cart\",LastExecution:77L,SuccessCount:2,TrackOutput:1b,UpdateLastExecution:1b}"),
            withName: false);
        var cartTag = new NbtCompound();
        cart.WriteNbt(cartTag);

        var copy = new CommandBlockMinecart { Level = context.World };
        copy.ReadNbt(cartTag);
        Assert.Equal("say cart", copy.Command);
        Assert.Equal(2, copy.SuccessCount);
        Assert.Equal(77, copy.Stored.LastExecution);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RespawnedPlayersAreToldTheirPermissionLevel()
    {
        await using var context = await StartAsync();
        var player = await JoinAsync(context, "Returner", 2, GameMode.Creative);
        await player.KillAsync(player, ChatMessage.Simple("Returner died"));
        ((TestClient)player.Client).ClearPackets();

        await player.RespawnAsync();

        // Vanilla's entity event 24 plus the level, after the respawn that gave the client a new player.
        var sent = ((TestClient)player.Client).SentPackets.ToList();
        var respawn = sent.FindIndex(packet => packet is RespawnPacket);
        var level = sent.FindIndex(packet => packet is EntityEventPacket { Event: 26 } entityEvent
            && entityEvent.EntityId == player.EntityId);
        Assert.InRange(respawn, 0, sent.Count - 1);
        Assert.True(level > respawn, "the permission level wasn't sent after the respawn");
    }

    private static async Task<RealServerContext> StartAsync()
    {
        var context = TestServerFactory.CreateServer($"command-block-safety-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.SpawnPosition = new VectorD(8, 64, 8);

        // The target's chunk and its neighbours, which its block ticks need.
        for (var x = -1; x <= 1; x++)
        {
            for (var z = -1; z <= 1; z++)
                WorldBlockEntities.AddEmptyChunk(world, x, z);
        }

        for (var x = -1; x <= 1; x++)
        {
            for (var z = -1; z <= 1; z++)
                await world.GetChunkAsync(x, z, scheduleGeneration: false);
        }

        return context;
    }

    // A player within reach of the target, with a permission level (kept in memory, not in ops.json) and game mode.
    private static async Task<ServerPlayer> JoinAsync(RealServerContext context, string name, int level, GameMode mode)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 64, 5.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        if (level > 0)
            AddOperator(context.Server, player, level);

        player.GameMode = mode;
        ((TestClient)player.Client).ClearPackets();
        return player;
    }

    private static void AddOperator(Server server, ServerPlayer player, int level)
    {
        var field = typeof(OperatorList).GetField("operators", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var entries = (IList)field.GetValue(server.Operators)!;
        var type = entries.GetType().GetGenericArguments()[0];
        var entry = Activator.CreateInstance(type)!;
        type.GetProperty("Uuid")!.SetValue(entry, player.Uuid);
        type.GetProperty("Username")!.SetValue(entry, player.Username);
        type.GetProperty("Level")!.SetValue(entry, level);
        entries.Add(entry);
    }

    private static void PlaceImpulse(WorldData.World world, Vector position, string command)
    {
        world.BlockUpdates.SetBlock(position, BlocksRegistry.CommandBlock.WithProperty("facing", "up"), Quiet);
        Entity(world, position).Command.SetCommand(command);
    }

    private static CommandBlockEntity Entity(WorldData.World world) => Entity(world, target);

    private static CommandBlockEntity Entity(WorldData.World world, Vector position) =>
        Assert.IsType<CommandBlockEntity>(world.GetResidentBlockEntity(position));

    // The chat a player was sent since it was last read, as text.
    private static string[] Chat(ServerPlayer player)
    {
        var client = (TestClient)player.Client;
        var chat = client.SentPackets
            .OfType<SystemChatPacket>()
            .Select(packet => StoredCommand.PlainText(packet.Message))
            .ToArray();
        client.ClearPackets();
        return chat;
    }

    // An editor's packet making the block an always active, unconditional repeating block that keeps its output.
    private static SetCommandBlockPacket EditPacket(Vector position, string command)
    {
        const int repeating = 1;
        const int trackOutputAndAutomatic = 1 | 4;

        using var buffer = new NetworkBuffer();
        buffer.WritePosition(position);
        buffer.WriteString(command);
        buffer.WriteVarInt(repeating);
        buffer.WriteByte((byte)trackOutputAndAutomatic);
        return SetCommandBlockPacket.Deserialize(buffer.AsSpan(0, buffer.Size).ToArray());
    }

    private static NbtCompound Without(NbtCompound tag, params string[] names)
    {
        var copy = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (!names.Contains(name))
                copy.Add(name, child);
        }

        return copy;
    }
}
