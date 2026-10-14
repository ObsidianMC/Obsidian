using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The environment attributes the server applies where they're asked about, from plugins' dimension types and
/// biomes as from vanilla's: beds explode where their bed rule says so, and respawn anchors where they don't work.
/// </summary>
public sealed class EnvironmentRules
{
    private const int Ground = 69;

    // A plugin's dimension type where beds explode with a message, and respawn anchors don't work (their default).
    private const string HotDimension = """
        {
          "has_skylight": true,
          "has_ceiling": false,
          "coordinate_scale": 1,
          "min_y": -64,
          "height": 384,
          "logical_height": 384,
          "infiniburn": "#minecraft:infiniburn_overworld",
          "ambient_light": 0,
          "monster_spawn_light_level": 7,
          "monster_spawn_block_light_limit": 0,
          "attributes": {
            "minecraft:gameplay/bed_rule": {
              "can_sleep": "never",
              "can_set_spawn": "never",
              "explodes": true,
              "error_message": { "text": "Too hot to sleep" }
            }
          }
        }
        """;

    // A plugin's biome where beds explode, in a dimension where they work.
    private const string BedlessBiome = """
        {
          "has_precipitation": false,
          "temperature": 2,
          "downfall": 0,
          "attributes": {
            "minecraft:gameplay/bed_rule": { "can_sleep": "never", "can_set_spawn": "never", "explodes": true },
            "minecraft:gameplay/snow_golem_melts": true
          },
          "effects": { "water_color": "#3f76e4" },
          "carvers": [],
          "features": [],
          "spawners": {},
          "spawn_costs": {}
        }
        """;

    // A plugin's biome where monsters don't burn.
    private const string ShadedBiome = """
        {
          "has_precipitation": false,
          "temperature": 0.5,
          "downfall": 0,
          "attributes": { "minecraft:gameplay/monsters_burn": false },
          "effects": { "water_color": "#3f76e4" },
          "carvers": [],
          "features": [],
          "spawners": {},
          "spawn_costs": {}
        }
        """;

    private const int OffhandSlot = 45;

    [Fact(DisplayName = "Beds and charged respawn anchors explode in a plugin's dimension where they don't work",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BedsExplodeInAPluginDimension()
    {
        await using var context = TestServerFactory.CreateServer($"beds-{Guid.NewGuid():N}");
        var world = context.World;

        using var document = JsonDocument.Parse(HotDimension);
        world.Initialize(new DimensionCodec
        {
            Name = "gameplaytests:hot",
            Id = -1,
            Element = DimensionTypeData.Read(document.RootElement).ToElement()
        });

        AddGroundChunk(world);
        var player = await JoinAsync(context);
        var client = (TestClient)player.Client;

        // Using the bed's foot reads the rule at its head, and removes both halves in the explosion.
        var foot = new Vector(8, Ground + 1, 8);
        var (footBlock, headBlock) = Bed();
        world.GetLoadedChunk(0, 0)!.SetBlock(foot, footBlock);
        world.GetLoadedChunk(0, 0)!.SetBlock(foot + new Vector(0, 0, -1), headBlock);

        Assert.True(await world.UseBedAsync(player, foot, footBlock));

        Assert.False(world.GetLoadedChunk(0, 0)!.GetBlock(foot).Is(BlockIds.RedBed));
        Assert.False(world.GetLoadedChunk(0, 0)!.GetBlock(foot + new Vector(0, 0, -1)).Is(BlockIds.RedBed));
        Assert.Contains(client.SentPackets.OfType<ExplodePacket>(), packet => packet.Radius == 5);
        Assert.Contains(
            client.SentPackets.OfType<SystemChatPacket>(),
            packet => packet.Overlay && packet.Message.Text == "Too hot to sleep");

        // Glowstone charges an anchor; a charged one explodes, since respawn_anchor_works is false by default.
        var anchor = new Vector(4, Ground + 1, 4);
        world.GetLoadedChunk(0, 0)!.SetBlock(anchor, BlocksRegistry.GetDefault(BlockIds.RespawnAnchor));
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Glowstone, 2));

        Assert.True(await UseAnchorAsync(world, player, anchor));
        Assert.Equal("1", Block(world, anchor).GetProperty("charges"));
        Assert.Equal(1, player.Inventory.GetItem(player.CurrentHeldItemSlot)?.Count);

        player.Inventory.SetItem(player.CurrentHeldItemSlot, null);
        Assert.True(await UseAnchorAsync(world, player, anchor));
        Assert.False(Block(world, anchor).Is(BlockIds.RespawnAnchor));
    }

    [Fact(DisplayName = "A plugin's biome's attributes apply where it is, over its dimension's",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BiomeAttributesApplyWhereTheBiomeIs()
    {
        await using var context = TestServerFactory.CreateServer($"bed-biome-{Guid.NewGuid():N}");
        var world = context.World;
        AddGroundChunk(world);

        PluginCodecs.Apply(
            [
                PluginCodecs.Parse(
                    PluginCodecs.Biome,
                    "gameplaytests:bedless",
                    "test",
                    Encoding.UTF8.GetBytes(BedlessBiome))
            ],
            NullLogger.Instance);

        Assert.True(CodecRegistry.TryGetBiome("gameplaytests:bedless", out var bedless));
        var player = await JoinAsync(context);

        // In the overworld's plains, a bed works and snow golems don't melt.
        var foot = new Vector(8, Ground + 1, 8);
        var (footBlock, headBlock) = Bed();
        world.GetLoadedChunk(0, 0)!.SetBlock(foot, footBlock);
        world.GetLoadedChunk(0, 0)!.SetBlock(foot + new Vector(0, 0, -1), headBlock);

        Assert.True(await world.UseBedAsync(player, foot, footBlock));
        Assert.True(Block(world, foot).Is(BlockIds.RedBed));
        Assert.False(world.Gameplay(LevelEnvironment.SnowGolemMelts, foot));

        // Where the plugin's biome is, it explodes, and snow golems melt. The bed rule is read at the bed's head.
        for (var y = Ground - 4; y < Ground + 8; y += 4)
        {
            world.GetLoadedChunk(0, 0)!.SetBiome(8, y, 4, bedless!);
            world.GetLoadedChunk(0, 0)!.SetBiome(8, y, 8, bedless!);
        }

        Assert.True(world.Gameplay(LevelEnvironment.SnowGolemMelts, foot));
        Assert.True(await world.UseBedAsync(player, foot, footBlock));
        Assert.False(Block(world, foot).Is(BlockIds.RedBed));
    }

    // A red bed facing north: its foot, and its head to the north.
    private static (IBlock Foot, IBlock Head) Bed()
    {
        var foot = BlocksRegistry.GetDefault(BlockIds.RedBed)
            .WithProperty("facing", "north")
            .WithProperty("part", "foot");

        return (foot, foot.WithProperty("part", "head"));
    }

    private static ValueTask<bool> UseAnchorAsync(World world, Entities.Player player, Vector anchor) =>
        world.UseRespawnAnchorAsync(player, anchor, Block(world, anchor), player.CurrentHeldItemSlot);

    private static IBlock Block(World world, Vector position) =>
        world.GetLoadedChunk(position.X >> 4, position.Z >> 4)!.GetBlock(position);

    private static async Task<Entities.Player> JoinAsync(RealServerContext context)
    {
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server,
            context.World,
            "Sleeper",
            new VectorD(8.5, Ground + 1, 12.5));

        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);

        return player;
    }

    // A complete plains chunk at the origin, empty but for a layer of dirt.
    private static void AddGroundChunk(World world)
    {
        var chunk = new Chunk(0, 0);

        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                chunk.SetBlock(x, Ground, z, BlocksRegistry.Dirt);

                if (x % 4 != 0 || z % 4 != 0)
                    continue;

                for (var y = -64; y < 320; y += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
    }
}
