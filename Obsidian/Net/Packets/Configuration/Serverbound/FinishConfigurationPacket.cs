using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using System.Diagnostics;

namespace Obsidian.Net.Packets.Configuration.Serverbound;
public sealed partial class FinishConfigurationPacket
{
    public async override ValueTask HandleAsync(Server server, Player player)
    {
        var client = player.client;

        client.Logger.LogDebug("Got finished configuration");

        client.SetState(ClientState.Play);
        await player.LoadAsync();
        if (!server.OnlinePlayers.TryAdd(player.Uuid, player))
            client.Logger.LogWarning("Failed to add player {Username} to online players. Undefined behavior ahead!", player.Username);

        if (!CodecRegistry.TryGetDimension(player.World.DimensionName, out var codec) || !CodecRegistry.TryGetDimension("minecraft:overworld", out codec))
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

        await client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(player.world.LevelData.SpawnPosition, 0));
        await client.QueuePacketAsync(new SetTimePacket(player.world.LevelData.Time, player.world.LevelData.DayTime, true));
        await client.QueuePacketAsync(new GameEventPacket(player.world.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));

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
            Flags = PositionFlags.None,
            TeleportId = player.TeleportId
        });

        await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));
        await player.UpdateChunksAsync(distance: 7);
        await server.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, server, DateTimeOffset.Now));
    }
}
