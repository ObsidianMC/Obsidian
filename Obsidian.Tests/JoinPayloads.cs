using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.ClientHandlers;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Login.Clientbound;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// What a client is sent from login to its first chunk, against vanilla's encoding of the same packets: the
/// asset generator's <c>join-payloads</c> fixture (<c>JoinFixtures.java</c>), which runs vanilla's codecs on the
/// inputs each row builds here.
/// </summary>
/// <remarks>
/// Runs alone, like the tests that override vanilla's dimension types for a while: the registries it sends are
/// vanilla's only when nothing overrides them.
/// </remarks>
[Collection(VanillaDimensionTypesCollection.Name)]
public class JoinPayloads
{
    private static readonly Lazy<JsonObject> fixture = new(() =>
    {
        var name = $"Obsidian.Assets.join-payloads-{ServerConstants.ProtocolDescription}.json";
        using var stream = typeof(NetworkBuffer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing {name}; build Obsidian.AssetGenerator.");
        return JsonNode.Parse(stream)!.AsObject();
    });

    public static TheoryData<string> Packets =>
    [
        "login_finished", "select_known_packs", "registry_data", "login", "respawn", "change_difficulty",
        "set_default_spawn_position", "set_time", "game_event", "player_abilities", "post_effects", "level_chunk",
        "light_masks", "chunks_biomes"
    ];

    [Theory]
    [MemberData(nameof(Packets))]
    public void PacketsMatchVanilla(string packet)
    {
        var expected = fixture.Value[packet] switch
        {
            JsonArray packets => packets.Select(node => node!.GetValue<string>()).ToList(),
            JsonObject chunk => [chunk["data"]!.GetValue<string>()],
            var single => [single!.GetValue<string>()]
        };

        var actual = packet == "light_masks" ? LightMasks() : Build(packet).Select(Encode).ToList();

        // The chunk is compared up to its light data, which the fixture doesn't have.
        if (packet == "level_chunk")
            actual = [actual[0][..Math.Min(actual[0].Length, expected[0].Length)]];

        if (packet == "registry_data")
        {
            AssertRegistriesMatch(expected, actual);
            return;
        }

        Assert.Equal(expected, actual);
    }

    // Clients disconnect when configuration finishes if a tag's registry isn't one they have, like villager_trade.
    [Fact]
    public async Task TagsAreOnlyForClientRegistries()
    {
        await using var context = TestServerFactory.CreateServer($"join-tags-{Guid.NewGuid():N}");
        var registries = context.Server.CreateDialogConfiguration().Tags.Keys;

        Assert.DoesNotContain("villager_trade", registries);
        Assert.Contains("item", registries);
        Assert.Contains("worldgen/biome", registries);
    }

    // Plugins' entries keep their ids for the life of the process, so registries other tests added to have those after
    // vanilla's, with their data. Vanilla's entries have to come first, in vanilla's order, and a registry nothing was
    // added to has to match byte for byte.
    private static void AssertRegistriesMatch(List<string> expected, List<string> actual)
    {
        Assert.Equal(expected.Select(packet => Decode(packet).Registry), actual.Select(packet => Decode(packet).Registry));

        foreach (var (vanilla, sent) in expected.Zip(actual))
        {
            var (registry, vanillaEntries) = Decode(vanilla);
            var sentEntries = Decode(sent).Entries;
            if (sentEntries.Count == vanillaEntries.Count)
            {
                Assert.Equal(vanilla, sent);
                continue;
            }

            Assert.Equal(vanillaEntries, sentEntries.Take(vanillaEntries.Count));
            Assert.All(sentEntries.Skip(vanillaEntries.Count), entry =>
                Assert.True(entry.Data && !entry.Id.StartsWith("minecraft:"), $"{registry} sent {entry.Id} after vanilla's"));
        }
    }

    // A registry data packet's registry and entries, each with whether it carries data.
    private static (string Registry, List<(string Id, bool Data)> Entries) Decode(string hex)
    {
        using var reader = new NetworkBuffer(Convert.FromHexString(hex));
        var registry = reader.ReadString();
        var entries = new List<(string, bool)>();
        for (var count = reader.ReadVarInt(); count > 0; count--)
        {
            var id = reader.ReadString();
            var data = reader.ReadBoolean();
            if (data)
                reader.ReadNbtCompound();
            entries.Add((id, data));
        }

        return (registry, entries);
    }

    private static IEnumerable<ClientboundPacket> Build(string packet) => packet switch
    {
        "login_finished" =>
        [
            new LoginFinishedPacket(Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"), "Fixture")
            {
                SkinProperties =
                [
                    new() { Name = "textures", Value = "dGV4dHVyZXM=", Signature = "c2lnbmF0dXJl" },
                    new() { Name = "cape", Value = "Y2FwZQ==" }
                ],
                SessionId = Guid.Parse("01234567-89ab-cdef-fedc-ba9876543210")
            }
        ],
        "select_known_packs" => [ConfigurationClientHandler.KnownPacksPacket()],
        // A server without plugins: vanilla's dialogs only.
        "registry_data" => ConfigurationClientHandler.RegistryPackets(
            CodecRegistry.Dialog.All.Values.OrderBy(dialog => dialog.Id).ToDictionary(dialog => dialog.Name, dialog => (ICodec)dialog)),
        "login" =>
        [
            new LoginPacket
            {
                EntityId = 301,
                Hardcore = true,
                DimensionNames = ["minecraft:overworld", "minecraft:the_nether", "minecraft:the_end"],
                MaxPlayers = 20,
                ViewDistance = 10,
                SimulationDistance = 8,
                ReducedDebugInfo = false,
                EnableRespawnScreen = true,
                DoLimitedCrafting = false,
                CommonPlayerSpawnInfo = new()
                {
                    DimensionType = CodecRegistry.Dimensions.Overworld.Id,
                    DimensionName = "minecraft:overworld",
                    HashedSeed = PlayerBase.HashSeed(1234567890123L),
                    GameMode = GameMode.Creative,
                    PreviousGamemode = GameMode.Survival,
                    Flat = true,
                    DeathLocation = new DeathLocation { DeathDimensionName = "minecraft:the_nether", Location = new Vector(-5, 70, 12) },
                    PortalCooldown = 7,
                    SeaLevel = 63
                },
                OnlineMode = true,
                EnforcesSecureChat = false
            }
        ],
        "respawn" =>
        [
            new RespawnPacket
            {
                CommonPlayerSpawnInfo = new()
                {
                    DimensionType = CodecRegistry.Dimensions.TheEnd.Id,
                    DimensionName = "minecraft:the_end",
                    HashedSeed = -42,
                    GameMode = GameMode.Spectator
                },
                DataKept = (DataKept)3
            }
        ],
        "change_difficulty" => [new ChangeDifficultyPacket(Difficulty.Hard) { DifficultyLocked = true }],
        "set_default_spawn_position" =>
        [
            new SetDefaultSpawnPositionPacket(new() { Dimension = "minecraft:overworld", Pos = new Vector(8, 64, -24) }, 90f, -12.5f)
        ],
        "set_time" =>
        [
            new SetTimePacket(123456789L,
            [
                new(VanillaSyncedRegistries.IdOf("minecraft:world_clock", "minecraft:overworld"), 30000L, 0f, 1f),
                new(VanillaSyncedRegistries.IdOf("minecraft:world_clock", "minecraft:the_end"), 777L, 0f, 0f)
            ])
        ],
        "game_event" =>
        [
            new GameEventPacket(ChangeGameStateReason.BeginRaining),
            new GameEventPacket(ChangeGameStateReason.RainLevelChange, 1f),
            new GameEventPacket(ChangeGameStateReason.ThunderLevelChange, 0.5f),
            new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks)
        ],
        "player_abilities" =>
        [
            new PlayerAbilitiesPacket
            {
                Abilities = PlayerAbility.Invulnerable | PlayerAbility.AllowFlying | PlayerAbility.CreativeMode,
                FlyingSpeed = 0.05f,
                WalkingSpeed = 0.1f
            }
        ],
        "post_effects" => [new PostEffectsPacket { PostEffects = [] }],
        "level_chunk" => [new LevelChunkWithLightPacket(Chunk(fixture.Value["level_chunk"]!.AsObject()))],
        "chunks_biomes" => [new ChunksBiomesPacket { ChunkBiomes = [new() { X = 3, Z = -2, Data = new byte[] { 1, 2, 3 } }] }],
        _ => throw new ArgumentException($"No packet for {packet}", nameof(packet))
    };

    // The section masks of the chunk and light packets: none, one section, a whole level's 26 and one past the first
    // long.
    private static List<string> LightMasks()
    {
        int[][] masks = [[], [1], [.. Enumerable.Range(0, 26)], [3, 70]];
        return
        [
            .. masks.Select(bits =>
            {
                var mask = new BitSet();
                foreach (var bit in bits)
                    mask.SetBit(bit, true);

                using var buffer = new NetworkBuffer();
                LightUpdatePacket.WriteMask(buffer, mask);
                return Convert.ToHexStringLower(buffer.AsSpan(0, buffer.Size));
            })
        ];
    }

    // The fixture's chunk: its blocks, vanilla's heights for them, and plains, vanilla's default biome, throughout.
    private static Chunk Chunk(JsonObject input)
    {
        var chunk = new Chunk(input["x"]!.GetValue<int>(), input["z"]!.GetValue<int>());
        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
        {
            for (var x = 0; x < 16; x += 4)
            {
                for (var z = 0; z < 16; z += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        foreach (var placement in input["placements"]!.AsArray())
        {
            var (x, y, z, state) = (placement![0]!.GetValue<int>(), placement[1]!.GetValue<int>(),
                placement[2]!.GetValue<int>(), placement[3]!.GetValue<int>());
            chunk.SetBlock(x, y, z, BlocksRegistry.Get(state));
        }

        foreach (var (name, heights) in input["heightmaps"]!.AsObject())
        {
            var heightmap = chunk.Heightmaps[Enum.Parse<HeightmapType>(name.Replace("_", ""), ignoreCase: true)];
            for (var i = 0; i < 256; i++)
                heightmap.Set(i & 15, i >> 4, heights![i]!.GetValue<int>());
        }

        return chunk;
    }

    private static string Encode(ClientboundPacket packet)
    {
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        return Convert.ToHexStringLower(buffer.AsSpan(0, buffer.Size));
    }
}
