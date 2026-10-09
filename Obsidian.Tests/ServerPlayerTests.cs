using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class ServerPlayerTests
{
    [Fact]
    public async Task ServerPlayer_CanBeConstructedWithoutANetworkConnection()
    {
        var worldName = $"server-player-{Guid.NewGuid():N}";
        var world = TestWorldFactory.Create(worldName);
        try
        {
            var server = new FakeServer(world);
            var player = new ServerPlayer(Guid.NewGuid(), "ServerBot", server, world)
            {
                Position = world.LevelData.SpawnPosition
            };

            Assert.False(player.Client.Connected);
            Assert.Same(player, player.Client.Player);
            Assert.Null(player.ClientIP);
            Assert.Equal("ServerBot", player.Username);
        }
        finally
        {
            await world.DisposeAsync().AsTask();
            DeleteWorld(worldName);
        }
    }

    [Fact]
    public async Task ServerPlayer_CanRegisterTickLookupAndCleanup()
    {
        var worldName = $"server-player-{Guid.NewGuid():N}";
        var world = TestWorldFactory.Create(worldName);
        try
        {
            var server = new FakeServer(world);
            var player = (ServerPlayer)await server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", world);
            player.Position = world.LevelData.SpawnPosition;

            Assert.True(server.IsPlayerOnline(player.Uuid));
            Assert.True(world.Players.ContainsKey(player.Uuid));
            Assert.Same(player, server.GetPlayer(player.Uuid));
            Assert.Same(player, server.GetPlayer(player.Username));
            Assert.Same(player, server.GetPlayer(player.EntityId));

            await world.DoWorldTickAsync();

            Assert.True(await server.RemoveServerPlayerAsync(player));
            Assert.False(server.IsPlayerOnline(player.Uuid));
            Assert.False(world.Players.ContainsKey(player.Uuid));
        }
        finally
        {
            await world.DisposeAsync().AsTask();
            DeleteWorld(worldName);
        }
    }

    private static void DeleteWorld(string worldName)
    {
        var path = Path.Combine("worlds", worldName);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
