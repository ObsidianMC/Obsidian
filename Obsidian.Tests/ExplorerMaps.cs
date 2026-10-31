using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData.Maps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class ExplorerMaps
{
    // The map system's smoke test: an explorer map from loot reaches the client and keeps updating while carried.
    [Fact]
    public async Task ExplorationLootKeepsItsIdentityAndUpdatesWhileCarried()
    {
        await using var server = TestServerFactory.CreateServer($"explorer-map-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Explorer");
        server.World.TryAddPlayer(player);
        var client = Assert.IsType<TestClient>(player.Client);
        var storage = MapStorage.For(server.World);
        var item = ItemsRegistry.FilledMap;
        var stack = new ItemStack(item, 2);
        var function = new ExplorationMapFunction { Decoration = "minecraft:red_x" };
        var context = new LootContext
        {
            Random = new LegacyRandomSource(1),
            Origin = player.Position,
            ExplorationMaps = new LocatedMapFactory(storage)
        };

        var map = function.Apply(stack, context);
        var id = Assert.IsType<int>(MapStorage.GetMapId(map));
        var data = Assert.IsType<MapData>(storage.Get(id));
        player.Inventory.SetItem(9, map);
        await TickMapsAsync(storage, player, 1);

        Assert.Same(stack, map);
        Assert.Same(item, map.Holder);
        Assert.Equal(2, map.Count);
        var first = Assert.Single(client.SentPackets.OfType<MapItemDataPacket>());
        Assert.Equal(id, first.MapId);
        Assert.NotNull(first.Patch);
        Assert.Contains(first.Decorations!, icon => icon.Type == MapDecorationType.RedX);
        var firstPlayer = Assert.Single(first.Decorations!, icon => icon.Type == MapDecorationType.Player);

        client.ClearPackets();
        player.Position += new VectorD(16, 0, 0);
        data.SetColor(3, 4, 12);
        await TickMapsAsync(storage, player, 5);

        var updates = client.SentPackets.OfType<MapItemDataPacket>().ToArray();
        var update = Assert.Single(updates, packet => packet.Patch is not null);
        Assert.Equal(id, update.MapId);
        Assert.Equal(new byte[] { 12 }, update.Patch!.Colors);
        var markers = Assert.Single(updates, packet => packet.Decorations is not null).Decorations!;
        var movedPlayer = Assert.Single(markers, icon => icon.Type == MapDecorationType.Player);
        Assert.NotEqual(firstPlayer.X, movedPlayer.X);
        Assert.Contains(markers, icon => icon.Type == MapDecorationType.RedX);
        Assert.Same(item, map.Holder);
    }

    private static async Task TickMapsAsync(MapStorage storage, IPlayer player, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            await storage.TickAsync(player);
        }
    }

    // Isolate the Obsidian integration after a successful structure lookup: saved data flows through the loot
    // function into the inventory tick. Structure placement and biome previews have separate vanilla captures.
    private sealed class LocatedMapFactory(MapStorage storage) : IExplorationMapFactory
    {
        public ItemStack Create(ExplorationMapRequest request)
        {
            var map = storage.CreateMap(
                0,
                0,
                (byte)request.Zoom,
                trackingPosition: true,
                unlimitedTracking: true,
                "minecraft:overworld");
            map[DataComponentType.MapDecorations] = new MapDecorationDataComponent
            {
                Decorations = new Dictionary<string, MapDecoration>
                {
                    ["+"] = new MapDecoration
                    {
                        Type = MapDecorationType.RedX,
                        X = 0,
                        Z = 0,
                        Rotation = 180
                    }
                }
            };

            return map;
        }
    }
}
