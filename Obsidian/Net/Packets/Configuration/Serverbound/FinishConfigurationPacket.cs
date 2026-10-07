using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics;

namespace Obsidian.Net.Packets.Configuration.Serverbound;
public sealed partial class FinishConfigurationPacket
{
    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var client = player.Client;

        client.SetState(ClientState.Play);
        await player.LoadAsync();

        // An integrated server opened to LAN forces its game mode on players who join.
        if (((Server)server).Integrated?.GetForcedGameMode(player.Level.LevelData) is GameMode forcedGameMode)
            player.GameMode = forcedGameMode;

        if (!server.AddPlayer(player))
        {
            await player.DisconnectAsync("Unable to complete login due to a server error. Please try again or contact an administrator.");
            Log.AddPlayerFailed(client.Logger, player.Username);
            return;
        }

        if (!CodecRegistry.TryGetDimension(player.Level.DimensionName, out var codec)
            || !CodecRegistry.TryGetDimension("minecraft:overworld", out codec))
            throw new UnreachableException("Failed to retrieve proper dimension for player.");

        await client.QueuePacketAsync(new LoginPacket
        {
            EntityId = player.EntityId,
            Hardcore = player.Level.LevelData.Hardcore,
            DimensionNames = CodecRegistry.Dimensions.All.Keys.ToList(),
            CommonPlayerSpawnInfo = new()
            {
                GameMode = player.GameMode,
                DimensionType = codec.Id,
                DimensionName = codec.Name,
                HashedSeed = 0,
                Flat = false
            },
            ReducedDebugInfo = false,
            EnableRespawnScreen = true,
        });

        // Like vanilla's PlayerList.placeNewPlayer, the player learns the world's difficulty right after joining.
        await client.QueuePacketAsync(ChangeDifficultyPacket.Of(server.DefaultWorld.LevelData));

        await client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            Dimension = codec.Name,
            Pos = (Vector)player.Level.LevelData.SpawnPosition.Floor()
        }, 0, 0));
        await client.QueuePacketAsync(new SetTimePacket(player.Level.LevelData.Time, player.Level.LevelData.DayTime, true));
        await client.QueuePacketAsync(new GameEventPacket(player.Level.LevelData.Raining
            ? ChangeGameStateReason.BeginRaining
            : ChangeGameStateReason.EndRaining));

        await client.QueuePacketAsync(CustomPayloadPacket.ClientboundPlay with { Channel = "minecraft:brand", PluginData = server.BrandData });

        // Like vanilla's PlayerList.placeNewPlayer, the player learns their permission level before the commands.
        var permissionLevel = ((OperatorList)server.Operators).GetPermissionLevel(player);
        await client.QueuePacketAsync(EntityEventPacket.PermissionLevel(player.EntityId, permissionLevel));
        await client.QueuePacketAsync(CommandsRegistry.Packet);

        await player.UpdatePlayerInfoAsync();
        await player.SendPlayerInfoAsync();

        // Like a teleport (Player.TeleportAsync), the join position is where the player was last, so the movement
        // handlers don't take the player's first movement for a meaningless one and drop every later one.
        player.LastPosition = player.Position;
        player.TeleportId = Globals.Random.Next(0, 999);
        await client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = player.Position,
            Yaw = 0,
            Pitch = 0,
            TeleportId = player.TeleportId
        });

        await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));
        await player.UpdateChunksAsync();
        await server.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, server, DateTimeOffset.Now));
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnected {Username}: adding them to the online players failed")]
        public static partial void AddPlayerFailed(ILogger logger, string username);
    }
}
