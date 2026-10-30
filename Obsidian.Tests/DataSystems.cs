using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class DataSystems
{
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly Lazy<JsonElement> capture = new(LoadCapture);

    private sealed record TransformerCase(
        string Transformer,
        JsonElement Input,
        string Above,
        string Face,
        JsonElement Output,
        string? Sound,
        int Damage,
        string? Loot);

    private sealed record MushroomCase(string Below, int Light, bool Survives);

    private sealed record ClockStateCase(
        long TotalTicks = 0,
        float PartialTick = 0,
        float Rate = 1,
        bool Paused = false);

    private sealed record ClockCase(ClockStateCase Initial, ClockStateCase[] Ticks);

    private sealed record MarkerCase(string Name, long Time, long Next, bool At);

    private sealed record TrackCase(
        string Timeline,
        string Attribute,
        long Time,
        JsonElement Initial,
        JsonElement Value);

    private sealed record PotteryCase(string Item, string Pattern, string Asset);

    private static JsonElement LoadCapture()
    {
        using var stream = typeof(AbstractLevel).Assembly
            .GetManifestResourceStream("Obsidian.Assets.data-systems-26.3.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static IEnumerable<T> Cases<T>(string section) =>
        capture.Value.GetProperty(section).Deserialize<T[]>(options)!;

    [Fact]
    public void TransformerProvidersMatchVanilla()
    {
        foreach (var sample in Cases<TransformerCase>("transformers"))
        {
            var input = ItemUseOnBehaviors.ReadState(sample.Input);
            var above = BlocksRegistry.Get(sample.Above);
            var face = Enum.Parse<BlockFace>(sample.Face, ignoreCase: true);
            IBlock Read(Vector position) => position.Y == 1 ? above : input;

            var result = ItemUseOnBehaviors.Evaluate(sample.Transformer, Read, Vector.Zero, face);

            AssertTransformation(sample, result);
        }
    }

    private static void AssertTransformation(TransformerCase sample, ItemUseOnBehaviors.Transformation? result)
    {
        if (sample.Output.ValueKind == JsonValueKind.Undefined)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        var expected = ItemUseOnBehaviors.ReadState(sample.Output);
        Assert.Equal(expected.GetHashCode(), result.State.GetHashCode());
        Assert.Equal(sample.Sound, result.Sound);
        Assert.Equal(sample.Damage, result.Damage);
        Assert.Equal(sample.Loot, result.Loot);
    }

    [Fact]
    public void MushroomSurvivalMatchesVanilla()
    {
        foreach (var sample in Cases<MushroomCase>("mushrooms"))
        {
            var below = BlocksRegistry.Get(sample.Below);

            var survives = RandomTickBehaviors.MushroomsCanSurvive(below, sample.Light);

            Assert.True(survives == sample.Survives, $"{sample.Below}, light {sample.Light}");
        }
    }

    [Fact]
    public void ClockTicksMatchVanilla()
    {
        foreach (var sample in Cases<ClockCase>("clocks"))
        {
            var clock = Clock(sample.Initial);

            foreach (var expected in sample.Ticks)
            {
                clock.Tick();

                AssertClock(expected, clock);
            }
        }
    }

    private static WorldClockState Clock(ClockStateCase sample) => new()
    {
        TotalTicks = sample.TotalTicks,
        PartialTick = sample.PartialTick,
        Rate = sample.Rate,
        Paused = sample.Paused
    };

    private static void AssertClock(ClockStateCase expected, WorldClockState actual)
    {
        Assert.Equal(expected.TotalTicks, actual.TotalTicks);
        Assert.Equal(expected.PartialTick, actual.PartialTick);
        Assert.Equal(expected.Rate, actual.Rate);
        Assert.Equal(expected.Paused, actual.Paused);
    }

    [Fact]
    public void TimelineMarkersMatchVanilla()
    {
        foreach (var sample in Cases<MarkerCase>("markers"))
        {
            var marker = WorldTimelines.Markers[sample.Name];

            var next = marker.Next(sample.Time);
            var at = marker.OccursAt(sample.Time);

            Assert.Equal(sample.Next, next);
            Assert.Equal(sample.At, at);
        }
    }

    [Fact]
    public async Task GameplayTimelineTracksMatchVanilla()
    {
        await using var context = TestServerFactory.CreateServer($"timeline-{Guid.NewGuid():N}");
        var world = context.World;

        foreach (var sample in Cases<TrackCase>("tracks"))
        {
            world.LevelData.Time = sample.Time;

            AssertTrack(world, sample);
        }
    }

    private static void AssertTrack(World world, TrackCase sample)
    {
        if (sample.Initial.ValueKind == JsonValueKind.Number)
        {
            var actual = WorldTimelines.Apply(world, sample.Attribute, sample.Initial.GetSingle());

            Assert.Equal(sample.Value.GetSingle(), actual);
            return;
        }

        var value = WorldTimelines.Apply(world, sample.Attribute, sample.Initial.GetBoolean());

        Assert.Equal(sample.Value.GetBoolean(), value);
    }

    [Fact]
    public void PotteryPatternsMatchVanilla()
    {
        foreach (var sample in Cases<PotteryCase>("pottery"))
        {
            var item = ItemsRegistry.Get(sample.Item);
            var stack = new ItemStack(item);

            var pattern = PotteryPatterns.Pattern(stack);
            var asset = PotteryPatterns.Asset(stack);

            Assert.Equal(sample.Pattern, pattern);
            Assert.Equal(sample.Asset, asset);
        }
    }

    [Fact]
    public void TrimPalettesMatchVanilla()
    {
        foreach (var sample in capture.Value.GetProperty("trims").EnumerateObject())
        {
            var expected = sample.Value;
            var material = CodecRegistry.TrimMaterial.All[sample.Name];

            using var writer = new RawNbtWriter(string.Empty);
            material.WriteElement(writer);
            writer.EndCompound();
            var encoded = (NbtCompound)NbtReader.ReadTag(writer.Data, readName: true, out _)!;

            Assert.Equal(expected.GetProperty("palette_id").GetString(), encoded.GetString("palette_id"));
            var description = expected.GetProperty("description");
            var text = Assert.IsType<NbtCompound>(encoded["description"]);
            Assert.Equal(description.GetProperty("translate").GetString(), text.GetString("translate"));
            Assert.Equal(description.GetProperty("color").GetString(), text.GetString("color"));
        }
    }

    [Fact]
    public async Task TransformerUseCommitsCapturedStateAndDamage()
    {
        // Prepare a real packet use from a captured rooted-dirt case.
        var sample = Cases<TransformerCase>("transformers").First(row =>
            row.Transformer == "minecraft:hoe"
            && row.Input.GetProperty("id").GetString() == "minecraft:rooted_dirt"
            && row.Above == "minecraft:stone"
            && row.Face == "down");
        await using var context = TestServerFactory.CreateServer($"transformer-{Guid.NewGuid():N}");
        var world = context.World;
        var position = new Vector(8, 70, 8);
        AddChunk(world);
        var input = ItemUseOnBehaviors.ReadState(sample.Input);
        var above = BlocksRegistry.Get(sample.Above);
        var chunk = world.GetLoadedChunk(0, 0)!;
        chunk.SetBlock(position, input);
        chunk.SetBlock(position + Vector.Up, above);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Farmer", new VectorD(8, 71, 9));
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.IronHoe));

        // Run the serverbound packet, including component lookup and inventory wear.
        var packet = BlockUpdates.UseOn(position, BlockFace.Down);
        await packet.HandleAsync(context.Server, player);

        // The committed block and wear must agree with the captured transformer.
        var expected = ItemUseOnBehaviors.ReadState(sample.Output);
        var actual = world.GetLoadedChunk(0, 0)!.GetBlock(position);
        var heldItem = player.Inventory.GetItem(player.CurrentHeldItemSlot)!;
        Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
        Assert.Equal(sample.Damage, heldItem.Damage);

        // Publish queued item entities before checking the interaction's loot.
        await world.DoWorldTickAsync();
        var drops = world.GetNonPlayerEntitiesInRange((VectorD)position, 3).OfType<Entities.ItemEntity>();
        Assert.Contains(drops, entity =>
            entity.Item.Holder == ItemsRegistry.HangingRoots);
    }

    private static void AddChunk(World world)
    {
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
        world.Fluids.Track(chunk);
    }

    [Fact]
    public void RegistryComponentsHonorOverridesAndSurviveSaving()
    {
        // Apply components to an ordinary item, then reload its persistent patch.
        var transformer = ComponentBuilder.BlockTransformer with { Value = "minecraft:axe" };
        var pottery = ComponentBuilder.ProvidesPotteryPattern with { Value = "minecraft:angler" };
        var original = new ItemStack(ItemsRegistry.Stick, 1, transformer, pottery);

        var saved = original.ToNbt();
        var reloaded = saved.ItemFromNbt()!;

        Assert.Equal("minecraft:axe", ItemUseOnBehaviors.Component(
            reloaded, DataComponentType.BlockTransformer, "block_transformer"));
        Assert.Equal("minecraft:angler", PotteryPatterns.Pattern(reloaded));

        // Removing defaults must prevent fallback to the item's vanilla components.
        var hoe = new ItemStack(ItemsRegistry.IronHoe);
        var sherd = new ItemStack(ItemsRegistry.AnglerPotterySherd);
        hoe.Remove(DataComponentType.BlockTransformer);
        sherd.Remove(DataComponentType.ProvidesPotteryPattern);

        Assert.Null(ItemUseOnBehaviors.Component(hoe, DataComponentType.BlockTransformer, "block_transformer"));
        Assert.Null(PotteryPatterns.Pattern(sherd));
    }

    [Fact]
    public void IndependentClocksSurviveSaveAndProduceCapturedSync()
    {
        // Decode the real vanilla full-sync fixture into persistent clock state.
        using var stream = typeof(AbstractLevel).Assembly
            .GetManifestResourceStream("Obsidian.Assets.join-payloads-26.3.json")!;
        using var fixture = JsonDocument.Parse(stream);
        var expected = fixture.RootElement.GetProperty("set_time").GetString()!;
        var data = ReadClockPacket(expected);
        var files = new LevelFiles();

        VanillaLevelData.Update(files, data);
        var reloaded = VanillaLevelData.Read(files);
        var packet = SetTimePacket.Of(reloaded);

        using var writer = new NetworkBuffer();
        packet.Serialize(writer);
        var actual = Convert.ToHexStringLower(writer.AsSpan(0, writer.Size));
        Assert.Equal(expected, actual);
    }

    private static LevelData ReadClockPacket(string wire)
    {
        var bytes = Convert.FromHexString(wire);
        using var reader = new NetworkBuffer(bytes);
        var data = new LevelData();
        data.WorldClocks.GameTime = reader.ReadLong();
        var count = reader.ReadVarInt();
        var registry = VanillaSyncedRegistries.All.First(entry => entry.Name == "minecraft:world_clock");

        for (var index = 0; index < count; index++)
        {
            var id = reader.ReadVarInt();
            var state = new WorldClockState
            {
                TotalTicks = reader.ReadVarLong(),
                PartialTick = reader.ReadSingle()
            };
            var rate = reader.ReadSingle();
            state.Paused = rate == 0;
            if (rate > 0)
                state.Rate = rate;

            data.WorldClocks.States[registry.Entries[id]] = state;
        }
        return data;
    }
}
