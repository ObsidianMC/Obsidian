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
        if (!server.AddPlayer(player))
        {
            await player.DisconnectAsync("Unable to complete login due to a server error. Please try again or contact an administrator.");
            Log.AddPlayerFailed(client.Logger, player.Username);
            return;
        }

        if (!CodecRegistry.TryGetDimension(player.Level.DimensionName, out var codec) && !CodecRegistry.TryGetDimension("minecraft:overworld", out codec))
            throw new UnreachableException("Failed to retrieve proper dimension for player.");

        await client.QueuePacketAsync(new LoginPacket
        {
            EntityId = player.EntityId,
            DimensionNames = CodecRegistry.Dimensions.All.Keys.ToList(),
            CommonPlayerSpawnInfo = new()
            {
                GameMode = player.GameMode,
                DimensionType = codec.Id,
                DimensionName = codec.Name,
                HashedSeed = 0,
                Flat = false,
                PortalCooldown = player is Entities.Player concrete ? concrete.PortalCooldown : 0
            },
            ReducedDebugInfo = false,
            EnableRespawnScreen = true,
        });

        var spawnLevel = player.Level is IDimension dimension ? dimension.ParentWorld : player.Level;
        await client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            Dimension = spawnLevel.DimensionName,
            Pos = (Vector)spawnLevel.LevelData.SpawnPosition.Floor()
        }, 0, 0));
        await client.QueuePacketAsync(new SetTimePacket(player.Level.LevelData.Time, player.Level.LevelData.DayTime, true));
        await client.QueuePacketAsync(new GameEventPacket(player.Level.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));

        await client.QueuePacketAsync(CustomPayloadPacket.ClientboundPlay with { Channel = "minecraft:brand", PluginData = server.BrandData });
        await client.QueuePacketAsync(CommandsRegistry.Packet);
        await client.QueuePacketAsync(new RecipeBookSettingsPacket());
        await client.QueuePacketAsync(new RecipeBookAddPacket());

        await player.UpdatePlayerInfoAsync();
        await player.SendPlayerInfoAsync();

        player.TeleportId = Globals.Random.Next(0, 999);
        await client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = player.Position,
            Yaw = player.Yaw,
            Pitch = player.Pitch,
            Flags = 0,
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
