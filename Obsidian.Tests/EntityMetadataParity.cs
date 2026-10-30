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
using Xunit;

namespace Obsidian.Tests;

public class EntityMetadataParity
{
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
            yield return ["entity", type.ToString()];

        using var fixture = Fixture();
        foreach (var serializer in fixture.RootElement.GetProperty("serializerNames").EnumerateObject())
            yield return ["serializer", serializer.Value.GetString()!];
    }

    [Fact]
    public void MetadataValuesMatchVanilla()
    {
        using var fixture = Fixture();
        var samples = fixture.RootElement.GetProperty("serializers");
        // Vanilla's captured holder samples select registry entry zero. In particular, animal variants use raw
        // registry ids, unlike the id-plus-one representation used by holders that also allow inline values.
        Entity[] entities =
        [
            new Cat { Level = null!, Variant = 0 }, new Cow { Level = null!, Variant = 0 }, new Chicken { Level = null!, Variant = 0 },
            new Pig { Level = null!, Variant = 0, SoundVariant = 0 }, new Wolf { Level = null!, Variant = 0, SoundVariant = 0 }, new Frog { Level = null!, Variant = 0 },
            new ZombieNautilus { Level = null!, Variant = 0 }, new Cushion { Level = null!, Color = 11 },
            new Interaction { Level = null!, Width = 1.25f, Height = 1.25f, Response = true },
            new SulfurCube { Level = null!, Size = 300, MaxFuse = 300, FromBucket = true },
            new Hoglin { Level = null!, IsImmuneToZombification = true }
        ];
        foreach (var entity in entities)
        {
            using var writer = new MetadataWriter();
            entity.Write(writer);
            var checkedFields = 0;
            foreach (var (index, serializer) in writer.Fields)
            {
                if (serializer is >= EntityMetadataType.CatVariant and <= EntityMetadataType.ZombieNautilusVariant
                    or EntityMetadataType.DyeColor
                    || entity is Interaction && index >= 8 || entity is SulfurCube or Hoglin && index >= 18)
                {
                    var expected = Convert.FromHexString(samples.GetProperty(((int)serializer).ToString()).GetString()!);
                    Assert.Equal(expected, writer.Value(index));
                    checkedFields++;
                }
            }
            Assert.True(checkedFields > 0, $"No payload checked for {entity.GetType().Name}.");
        }

        // Populated values the defaults never write, matched against vanilla's samples: a negative (sitting) pose
        // timer for LONG's -1, an owner for OPTIONAL_LIVING_ENTITY_REFERENCE's UUID and a nonzero INT variant for 300.
        var owner = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        (Entity Entity, byte Index)[] populated =
        [
            (new Camel { Level = null!, LastPoseChangeTick = -1 }, 20), (new Cat { Level = null!, Owner = owner }, 19),
            (new Wolf { Level = null!, Owner = owner }, 19), (new Horse { Level = null!, Variant = 300 }, 19)
        ];
        foreach (var (entity, index) in populated)
        {
            using var writer = new MetadataWriter();
            entity.Write(writer);
            var serializer = Assert.Single(writer.Fields, field => field.Index == index).Serializer;
            Assert.Equal(Convert.FromHexString(samples.GetProperty(((int)serializer).ToString()).GetString()!), writer.Value(index));
        }

        // The holder samples only select entry zero, so check that nonzero raw registry ids reach the wire as well.
        using var cat = new MetadataWriter();
        new Cat { Level = null!, Variant = 10 }.Write(cat);
        Assert.Equal([10], cat.Value(20));
        using var wolf = new MetadataWriter();
        new Wolf { Level = null!, Variant = 8, SoundVariant = 6 }.Write(wolf);
        Assert.Equal([8], wolf.Value(23));
        Assert.Equal([6], wolf.Value(24));

        // Vanilla's default pig sound is classic: raw registry id one, after big at zero.
        using var defaultPig = new MetadataWriter();
        new Pig { Level = null! }.Write(defaultPig);
        Assert.Equal(1, Assert.Single(defaultPig.Value(20)));

        // Distinguish the two flags even though both use BOOLEAN, including on slime's new ageable base.
        foreach (AgeableMob entity in new AgeableMob[] { new Slime { Level = null! }, new MagmaCube { Level = null! }, new Cow { Level = null! } })
        {
            entity.IsBaby = true;
            entity.AgeLocked = false;
            using var baby = new MetadataWriter();
            entity.Write(baby);
            Assert.Equal(1, Assert.Single(baby.Value(16)));
            Assert.Equal(0, Assert.Single(baby.Value(17)));

            entity.IsBaby = false;
            entity.AgeLocked = true;
            using var locked = new MetadataWriter();
            entity.Write(locked);
            Assert.Equal(0, Assert.Single(locked.Value(16)));
            Assert.Equal(1, Assert.Single(locked.Value(17)));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
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

        // These implementations are created outside EntitySpawner.
        Entity entity = type switch
        {
            EntityType.Player => new ServerPlayer(Guid.Empty, "MetadataFixture", null!, null!),
            EntityType.Item => new ItemEntity { Level = null! },
            EntityType.FallingBlock => new FallingBlock(VectorD.Zero) { Level = null!, Block = BlocksRegistry.Sand },
            _ => EntitySpawner.Create(type, null!)
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
        // The ageable hierarchy and the newly implemented types write their complete layout. This also catches
        // omitted fields and same-serializer shifts that a compatibility-only subset check would miss.
        if (entity is AgeableMob or Cushion or Interaction or OminousItemSpawner or DisplayEntity
            || type is EntityType.Marker or EntityType.WindCharge or EntityType.PoplarBoat or EntityType.PoplarChestBoat)
            Assert.Equal(layout.EnumerateObject().Select(field => int.Parse(field.Name)).Order(),
                writer.Fields.Select(field => (int)field.Index).Order());

    }

    // Reimplement just the header method on the interface; inherited NetworkBuffer methods still write real payloads.
    // This avoids duplicating the serializer decoder (which could hide or misdiagnose a shifted header).
    private sealed class MetadataWriter : NetworkBuffer, INetStreamWriter
    {
        public List<(byte Index, EntityMetadataType Serializer)> Fields { get; } = [];
        public Dictionary<byte, int> ValueOffsets { get; } = [];
        private Dictionary<byte, int> HeaderOffsets { get; } = [];

        public new void WriteEntityMetadataType(byte index, EntityMetadataType type)
        {
            Fields.Add((index, type));
            HeaderOffsets[index] = Offset;
            base.WriteEntityMetadataType(index, type);
            ValueOffsets[index] = Offset;
        }

        public byte[] Value(byte index)
        {
            var position = Fields.FindIndex(field => field.Index == index);
            var end = position + 1 < Fields.Count ? HeaderOffsets[Fields[position + 1].Index] : Offset;
            return ToArray()[ValueOffsets[index]..end];
        }
    }
}
