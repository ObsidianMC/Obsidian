using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics;

namespace Obsidian.Net.Packets.Configuration.Serverbound;

public sealed partial class FinishConfigurationPacket
{
    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
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

        var joined = (Entities.Player)player;
        await client.QueuePacketAsync(new LoginPacket
        {
            EntityId = player.EntityId,
            Hardcore = player.Level.LevelData.Hardcore,
            MaxPlayers = server.Configuration.MaxPlayers,
            ViewDistance = server.Configuration.ViewDistance,
            SimulationDistance = server.Configuration.SimulationDistance,
            DimensionNames = LevelNames(server.WorldManager),
            CommonPlayerSpawnInfo = joined.CreateSpawnInfo(),
            ReducedDebugInfo = false,
            EnableRespawnScreen = true,
            OnlineMode = server.Configuration.OnlineMode,
        });

        // Like vanilla's PlayerList.placeNewPlayer, the player learns the world's difficulty right after joining.
        await client.QueuePacketAsync(ChangeDifficultyPacket.Of(server.DefaultWorld.LevelData));
        await client.QueuePacketAsync(WorldData.GameRuleSynchronization.Snapshot(player.Level.LevelData));

        var spawnLevel = player.Level is IDimension dimension ? dimension.ParentWorld : player.Level;
        await client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            Dimension = spawnLevel.DimensionName,
            Pos = (Vector)spawnLevel.LevelData.SpawnPosition.Floor()
        }, 0, 0));
        await client.QueuePacketAsync(SetTimePacket.Of(player.Level.LevelData));

        // Like vanilla's PlayerList.sendLevelInfo, a raining level's weather; a clear one sends none.
        if (player.Level.LevelData.Raining)
        {
            await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.BeginRaining));
            await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.RainLevelChange, 1));
            await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.ThunderLevelChange,
                player.Level.LevelData.Thundering ? 1 : 0));
        }

        await client.QueuePacketAsync(CustomPayloadPacket.ClientboundPlay with { Channel = "minecraft:brand", PluginData = server.BrandData });

        // Like vanilla's PlayerList.placeNewPlayer, the player learns their permission level before the commands.
        var permissionLevel = ((OperatorList)server.Operators).GetPermissionLevel(player);
        await client.QueuePacketAsync(EntityEventPacket.PermissionLevel(player.EntityId, permissionLevel));
        await client.QueuePacketAsync(CommandsRegistry.Packet);
        await client.QueuePacketAsync(new RecipeBookSettingsPacket());

        // Objectives and teams made before the player joined.
        ((Server)server).CommandScoreboard.SendTo(player);
        foreach (var team in server.ScoreboardManager.DefaultScoreboard.Teams.OfType<Team>())
            await client.QueuePacketAsync(team.CreationPacket());

        await joined.RecipeBookSync.SendInitialAsync(player);
        await joined.ProgressAdvancements.FlushAsync(initial: true);

        await player.UpdatePlayerInfoAsync();
        await player.SendPlayerInfoAsync();

        // Like a teleport (Player.TeleportAsync), the join position is where the player was last, so the movement
        // handlers don't take the player's first movement for a meaningless one and drop every later one.
        player.LastPosition = player.Position;
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
        // Obsidian has no post effects of its own; like vanilla, a joining player starts with none.
        await client.QueuePacketAsync(new PostEffectsPacket { PostEffects = [] });
        await server.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, server, DateTimeOffset.Now));
        await player.UpdateChunksAsync();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnected {Username}: adding them to the online players failed")]
        public static partial void AddPlayerFailed(ILogger logger, string username);
    }

    // Like vanilla's server.levelKeys(): the names of the levels players can be in, plugins' dimensions included,
    // which vanilla clients suggest in commands.
    private static List<string> LevelNames(IWorldManager worldManager) =>
    [
        .. worldManager.GetAvailableWorlds()
            .OfType<WorldData.World>()
            .SelectMany(world => world.dimensions.Values
                .Select(dimension => dimension.DimensionName)
                .Prepend(world.DimensionName))
            .Distinct()
    ];
}
