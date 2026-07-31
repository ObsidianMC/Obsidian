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

        client.Logger.LogDebug("Got finished configuration");

        client.SetState(ClientState.Play);
        await player.LoadAsync();
        if (!server.AddPlayer(player))
        {
            await player.DisconnectAsync("Unable to complete login due to a server error. Please try again or contact an administrator.");
            client.Logger.LogWarning("Failed to add player {Username} to online players. Disconnecting...", player.Username);
            return;
        }

        if (!CodecRegistry.TryGetDimension(player.Level.DimensionName, out var codec) || !CodecRegistry.TryGetDimension("minecraft:overworld", out codec))
            throw new UnreachableException("Failed to retrieve proper dimension for player.");

        await client.QueuePacketAsync(new LoginPacket
        {
            EntityId = player.EntityId,
            DimensionNames = CodecRegistry.Dimensions.All.Keys.ToList(),
            CommonPlayerSpawnInfo = new()
            {
                Gamemode = player.Gamemode,
                DimensionType = codec.Id,
                DimensionName = codec.Name,
                HashedSeed = 0,
                Flat = false
            },
            ReducedDebugInfo = false,
            EnableRespawnScreen = true,
        });

        await client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            DimensionName = codec.Name,
            Position = (Vector)player.Level.LevelData.SpawnPosition
        }, 0, 0));
        await client.QueuePacketAsync(new SetTimePacket(player.Level.LevelData.Time, player.Level.LevelData.DayTime, true));
        await client.QueuePacketAsync(new GameEventPacket(player.Level.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));

        await client.QueuePacketAsync(CustomPayloadPacket.ClientboundPlay with { Channel = "minecraft:brand", PluginData = server.BrandData });
        await client.QueuePacketAsync(CommandsRegistry.Packet);

        await player.UpdatePlayerInfoAsync();
        await player.SendPlayerInfoAsync();

        player.TeleportId = Globals.Random.Next(0, 999);
        await client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = player.Position,
            Yaw = 0,
            Pitch = 0,
            TeleportId = player.TeleportId
        });

        await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));
        await player.UpdateChunksAsync(distance: 7);
        await server.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, server, DateTimeOffset.Now));
    }
}
