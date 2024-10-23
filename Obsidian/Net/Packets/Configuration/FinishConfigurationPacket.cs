using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using System.Diagnostics;

namespace Obsidian.Net.Packets.Configuration;
public sealed partial class FinishConfigurationPacket : IServerboundPacket, IClientboundPacket
{
    public static FinishConfigurationPacket Default { get; } = new();

    public int Id => 0x03;

    public void Populate(byte[] data) { }
    public void Populate(MinecraftStream stream) { }

    public void Serialize(MinecraftStream stream) => this.WritePacketId(stream);

    public ValueTask HandleAsync(Client client) => default;

    public async ValueTask HandleAsync(Server server, Player player)
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
            Gamemode = player.Gamemode,
            DimensionNames = CodecRegistry.Dimensions.All.Keys.ToList(),
            DimensionType = codec.Id,
            DimensionName = codec.Name,
            HashedSeed = 0,
            ReducedDebugInfo = false,
            EnableRespawnScreen = true,
            Flat = false
        });

        await client.QueuePacketAsync(new PluginMessagePacket("minecraft:brand", server.BrandData));
        await client.QueuePacketAsync(CommandsRegistry.Packet);

        await client.QueuePacketAsync(new UpdateRecipeBookPacket
        {
            Action = UnlockRecipeAction.Init,
            FirstRecipeIds = RecipesRegistry.Recipes.Keys.ToList(),
            SecondRecipeIds = RecipesRegistry.Recipes.Keys.ToList()
        });

        await player.UpdatePlayerInfoAsync();
        await client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));

        player.TeleportId = Globals.Random.Next(0, 999);
        await client.QueuePacketAsync(new SynchronizePlayerPositionPacket
        {
            Position = player.Position,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = player.TeleportId
        });

        await player.UpdateChunksAsync(distance: 7);
        await player.SendInitialInfoAsync();
        await server.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, server, DateTimeOffset.Now));
    }
}
