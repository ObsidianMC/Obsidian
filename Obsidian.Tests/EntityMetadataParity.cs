#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Net;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Xunit;

namespace Obsidian.Tests;

public class EntityMetadataParity
{
    // These types have no implementation; the factory misclassifies them as Living. Fixing the factory / IsNonLiving
    // list is outside this metadata-writing change. Keep explicit skipped rows, rather than silently dropping types.
    private const string FactoryGap = "Known gap: no dedicated implementation; EntitySpawner/IsNonLiving incorrectly falls back to Living (outside metadata ownership).";
    private static bool HasFactoryGap(EntityType type) => type is EntityType.BlockDisplay or EntityType.ItemDisplay
        or EntityType.Interaction or EntityType.Marker or EntityType.OminousItemSpawner or EntityType.WindCharge;

    // 26.3's metadata, which Phase 2 of docs/porting-26.3-plan.md writes: AgeableMob gained AGE_LOCKED and slimes a
    // baby flag (shifting the indices after them), villagers an unhappy counter, and the cushion and poplar boats have
    // no implementation yet.
    private const string Phase2Metadata = "Phase 2: 26.3 changed or added this entity's metadata";
    private static bool HasPhase2Drift(EntityType type) => type is EntityType.Armadillo or EntityType.Axolotl
        or EntityType.Bee or EntityType.Camel or EntityType.CamelHusk or EntityType.Cat or EntityType.Chicken
        or EntityType.Cow or EntityType.Cushion or EntityType.Dolphin or EntityType.Donkey or EntityType.Fox
        or EntityType.Frog or EntityType.GlowSquid or EntityType.Horse or EntityType.Llama or EntityType.MagmaCube
        or EntityType.Mooshroom or EntityType.Mule or EntityType.Nautilus or EntityType.Panda or EntityType.Parrot
        or EntityType.Pig or EntityType.PoplarBoat or EntityType.PoplarChestBoat or EntityType.Rabbit
        or EntityType.Sheep or EntityType.SkeletonHorse or EntityType.Slime or EntityType.Sniffer
        or EntityType.Strider or EntityType.TraderLlama or EntityType.Villager or EntityType.Wolf
        or EntityType.ZombieHorse or EntityType.ZombieNautilus;

    private static JsonDocument Fixture()
    {
        var name = $"Obsidian.Assets.client-entity-metadata-{ServerConstants.ProtocolDescription}.json";
        using var stream = typeof(NetworkBuffer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing vanilla metadata fixture {name}; rebuild vanilla assets.");
        return JsonDocument.Parse(stream);
    }

    public static IEnumerable<object[]> Cases()
    {
        // Include even the factory's generic fallbacks, so adding an implementation needs no new test row.
        foreach (var type in Enum.GetValues<EntityType>())
            if (!HasFactoryGap(type) && !HasPhase2Drift(type))
                yield return ["entity", type.ToString()];

        using var fixture = Fixture();
        foreach (var serializer in fixture.RootElement.GetProperty("serializerNames").EnumerateObject())
            yield return ["serializer", serializer.Value.GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [InlineData("entity", nameof(EntityType.BlockDisplay), Skip = FactoryGap)]
    [InlineData("entity", nameof(EntityType.ItemDisplay), Skip = FactoryGap)]
    [InlineData("entity", nameof(EntityType.Interaction), Skip = FactoryGap)]
    [InlineData("entity", nameof(EntityType.Marker), Skip = FactoryGap)]
    [InlineData("entity", nameof(EntityType.OminousItemSpawner), Skip = FactoryGap)]
    [InlineData("entity", nameof(EntityType.WindCharge), Skip = FactoryGap)]
    [InlineData("entity", nameof(EntityType.Armadillo), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Axolotl), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Bee), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Camel), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.CamelHusk), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Cat), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Chicken), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Cow), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Cushion), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Dolphin), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Donkey), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Fox), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Frog), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.GlowSquid), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Horse), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Llama), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.MagmaCube), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Mooshroom), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Mule), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Nautilus), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Panda), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Parrot), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Pig), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.PoplarBoat), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.PoplarChestBoat), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Rabbit), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Sheep), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.SkeletonHorse), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Slime), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Sniffer), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Strider), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.TraderLlama), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Villager), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.Wolf), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.ZombieHorse), Skip = Phase2Metadata)]
    [InlineData("entity", nameof(EntityType.ZombieNautilus), Skip = Phase2Metadata)]
    public void MetadataMatchesVanilla(string kind, string name)
    {
        using var fixture = Fixture();
        var serializers = fixture.RootElement.GetProperty("serializerNames");
        if (kind == "serializer")
        {
            // These aliases preserve the public API's historical spellings, independently of enum generation.
            var apiName = name switch
            {
                "INT" => "VarInt",
                "LONG" => "VarLong",
                "COMPONENT" => "TextComponent",
                "OPTIONAL_COMPONENT" => "OptionalTextComponent",
                "ITEM_STACK" => "Slot",
                "OPTIONAL_UNSIGNED_INT" => "OptionalUnsignedVarInt",
                _ => string.Concat(name.Split('_').Select(part => part[0] + part[1..].ToLowerInvariant()))
            };
            var expected = serializers.EnumerateObject().Single(row => row.Value.GetString() == name).Name;
            Assert.True(Enum.TryParse<EntityMetadataType>(apiName, out var actual), $"Missing metadata serializer {name} ({apiName}).");
            Assert.True(int.Parse(expected) == (int)actual, $"Serializer {name}: vanilla id {expected}, Obsidian id {(int)actual} ({actual}).");
            Assert.Equal(serializers.EnumerateObject().Count(), Enum.GetValues<EntityMetadataType>().Length);
            return;
        }

        var type = Enum.Parse<EntityType>(name);
        var key = "minecraft:" + JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        Assert.True(fixture.RootElement.GetProperty("entities").TryGetProperty(key, out var layout),
            $"No vanilla layout for {key} in {ServerConstants.ProtocolDescription}.");

        // Hoglin's converting flag reads dimension/environment data while writing metadata.
        var level = type == EntityType.Hoglin ? TestWorldFactory.Create("metadata-hoglin") : null;
        // These implementations are created outside EntitySpawner.
        Entity entity = type switch
        {
            EntityType.Player => new ServerPlayer(Guid.Empty, "MetadataFixture", null!, null!),
            EntityType.Item => new ItemEntity { Level = null! },
            EntityType.FallingBlock => new FallingBlock(VectorD.Zero) { Level = null!, Block = BlocksRegistry.Sand },
            _ => EntitySpawner.Create(type, level!)
        };
        // Also cover implementations constructed elsewhere (e.g. a cast fishing bobber).
        var implementation = typeof(Entity).Assembly.GetTypes().SingleOrDefault(candidate =>
            !candidate.IsAbstract && typeof(Entity).IsAssignableFrom(candidate)
            && candidate.GetCustomAttribute<MinecraftEntityAttribute>(inherit: false)?.ResourceLocation == key);
        if (implementation is not null && !implementation.IsInstanceOfType(entity))
        {
            Assert.True(implementation.GetConstructor(Type.EmptyTypes) is not null,
                $"Add a metadata test constructor for {implementation.Name}; EntitySpawner uses {entity.GetType().Name} for {key}.");
            entity = (Entity)Activator.CreateInstance(implementation)!;
        }
        entity.Type = type;
        using var writer = new MetadataWriter();
        entity.Write(writer);
        Assert.NotEmpty(writer.Fields);

        foreach (var (index, serializer) in writer.Fields)
        {
            var context = $"{key} ({entity.GetType().Name}), metadata index {index}";
            Assert.True(layout.TryGetProperty(index.ToString(), out var field),
                $"{context}: Obsidian writes {serializer} ({(int)serializer}), but vanilla defines no accessor at this index.");
            var expected = field.GetProperty("serializer").GetInt32();
            Assert.True(expected == (int)serializer,
                $"{context} ({field.GetProperty("name")}): vanilla serializer {serializers.GetProperty(expected.ToString())} ({expected}), Obsidian {serializer} ({(int)serializer}).");
        }
        Assert.Equal(writer.Fields.Count, writer.Fields.Select(field => field.Index).Distinct().Count());
        if (entity is PlayerBase player)
        {
            // Exercise populated shoulders as well as clearing them on the default instance.
            player.LeftShoulder = new Parrot { Level = null!, Variant = 2 };
            player.RightShoulder = new Parrot { Level = null!, Variant = 4 };
            using var shoulders = new MetadataWriter();
            player.Write(shoulders);
            foreach (var index in new byte[] { 19, 20 })
            {
                var actual = Assert.Single(shoulders.Fields, field => field.Index == index);
                Assert.True(layout.GetProperty(index.ToString()).GetProperty("serializer").GetInt32() == (int)actual.Serializer,
                    $"{key}, shoulder index {index}: vanilla OPTIONAL_UNSIGNED_INT (19), Obsidian {actual.Serializer} ({(int)actual.Serializer}).");
                using var populated = new NetworkBuffer(shoulders.ToArray());
                populated.Offset = shoulders.ValueOffsets[index];
                Assert.Equal(index == 19 ? 3 : 5, populated.ReadVarInt());
                using var empty = new NetworkBuffer(writer.ToArray());
                empty.Offset = writer.ValueOffsets[index];
                Assert.Equal(0, empty.ReadVarInt());
            }
        }
        // Deliberately a subset check: vanilla defines fields Obsidian doesn't implement or only writes when dirty.
        // Payload/default-value equivalence is separate from accessor index/serializer compatibility.
    }

    // Reimplement just the header method on the interface; inherited NetworkBuffer methods still write real payloads.
    // This avoids duplicating the serializer decoder (which could hide or misdiagnose a shifted header).
    private sealed class MetadataWriter : NetworkBuffer, INetStreamWriter
    {
        public List<(byte Index, EntityMetadataType Serializer)> Fields { get; } = [];
        public Dictionary<byte, int> ValueOffsets { get; } = [];

        public new void WriteEntityMetadataType(byte index, EntityMetadataType type)
        {
            Fields.Add((index, type));
            base.WriteEntityMetadataType(index, type);
            ValueOffsets[index] = Offset;
        }
    }
}
