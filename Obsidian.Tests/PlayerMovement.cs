using Obsidian.API;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

// Plugins load like the block update tests' probe.
[Collection(nameof(PluginTagFiles))]
public sealed class PlayerMovement : IDisposable
{
    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-movement-{Guid.NewGuid():N}");

    public PlayerMovement() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(
        DisplayName = "A plugin's movement abilities jump, glide and fly, reach the client and go with the plugin",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task MovementAbilitiesFollowTheirPlugin()
    {
        await using var context = TestServerFactory.CreateServer($"movement-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = (await manager.LoadPluginAsync(path))!;

        var ground = new VectorD(0.5, BlockUpdates.Ground + 1, 0.5);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Jumper", ground);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.GameMode = GameMode.Survival;

        // An Obsidian client agrees on the movement channel.
        var client = (TestClient)player.Client;
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, new PayloadChannelRegistry());
        client.Channels = channels;
        using (var query = new NetworkBuffer())
        {
            ChannelPayloads.WriteQuery(query, new Dictionary<NetworkProtocol, List<NetworkComponent>>
            {
                [NetworkProtocol.Play] = [new(MovementAbilitiesPayload.Channel, "1", PacketFlow.Clientbound, true)]
            });
            await channels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, query.AsSpan(0, query.Size).ToArray());
        }

        Assert.True(await channels.FinishHandshakeAsync(0));
        client.SetState(ClientState.Play);
        client.ClearPackets();

        // The plugin grants them from its own code, which the grant is credited to.
        using (plugin.TryEnter())
        {
            player.SetMovementAbilities("probe:boots", new MovementAbilities { ExtraJumps = 1, CanGlide = true });
            player.SetMovementAbilities("probe:ring", new MovementAbilities { CanFly = true });
        }

        var granted = new MovementAbilities { ExtraJumps = 1, CanGlide = true, CanFly = true };
        Assert.Equal(granted, player.MovementAbilities);
        Assert.True(player.Abilities.HasFlag(PlayerAbility.AllowFlying));
        Assert.Contains(client.SentPackets, packet => packet is PlayerAbilitiesPacket);

        player.SynchronizeMovementAbilities();
        var payload = Assert.Single(client.SentPackets.OfType<CustomPayloadPacket>());
        Assert.Equal(MovementAbilitiesPayload.Channel, payload.Channel);
        using (var reader = new NetworkBuffer(payload.PluginData.ToArray()))
            Assert.Equal(granted with { CanFly = false }, MovementAbilitiesPayload.Decode(reader));

        // In the air, a second press of jump is an extra jump, which forgives the fall; the next one isn't.
        await player.UpdateAsync(ground + new VectorD(0, 3, 0), MovementFlags.None);
        await player.UpdateAsync(ground + new VectorD(0, 2, 0), MovementFlags.None);
        Assert.Equal(1, player.FallDistance);

        player.TrackJumpInput(0, PlayerInput.Jump);
        Assert.Equal(0, player.FallDistance);

        await player.UpdateAsync(ground + new VectorD(0, 1, 0), MovementFlags.None);
        player.TrackJumpInput(0, PlayerInput.Jump);
        Assert.Equal(1, player.FallDistance);

        // Reporting a landing in empty air cannot replenish the spent jump.
        await player.UpdateAsync(ground + new VectorD(0, 1, 0), MovementFlags.OnGround);
        await player.UpdateAsync(ground + new VectorD(0, 1, 0), MovementFlags.None);
        player.TrackJumpInput(0, PlayerInput.Jump);
        Assert.Equal(1, player.FallDistance);

        // It glides without an elytra, until it lands.
        player.TryStartGliding();
        Assert.True(player.FlyingWithElytra);

        await player.UpdateAsync(ground, MovementFlags.OnGround);
        Assert.False(player.FlyingWithElytra);

        // A slow glide down keeps the fall distance at 1, as vanilla's updateFallFlying does, so landing from it hurts nothing.
        player.TryStartGliding();
        await player.UpdateAsync(ground + new VectorD(0, 3, 0), MovementFlags.None);
        player.TryStartGliding();
        Assert.True(player.FlyingWithElytra);
        for (var y = 2.9; y >= 1; y -= 0.1)
            await player.UpdateAsync(ground + new VectorD(0, y, 0), MovementFlags.None);
        Assert.InRange(player.FallDistance, 1, 1.2);
        await player.UpdateAsync(ground, MovementFlags.OnGround);
        Assert.False(player.FlyingWithElytra);
        Assert.Equal(0, player.FallDistance);

        // A supported landing gives back the jump.
        await player.UpdateAsync(ground + new VectorD(0, 3, 0), MovementFlags.None);
        await player.UpdateAsync(ground + new VectorD(0, 2, 0), MovementFlags.None);
        player.TrackJumpInput(0, PlayerInput.Jump);
        Assert.Equal(0, player.FallDistance);

        // Unloading the plugin takes its grants back, flight included.
        await manager.UnloadPluginAsync(plugin);
        Assert.Equal(default, player.MovementAbilities);
        Assert.False(player.Abilities.HasFlag(PlayerAbility.AllowFlying));

        await player.UpdateAsync(ground + new VectorD(0, 3, 0), MovementFlags.None);
        player.TryStartGliding();
        Assert.False(player.FlyingWithElytra);
    }
}
