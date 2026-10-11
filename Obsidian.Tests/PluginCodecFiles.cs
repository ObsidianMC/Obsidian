using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Registries;
using System.IO;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' biome, dimension type and damage type files, added to the codec registries like a data pack's entries.
/// </summary>
public class PluginCodecFiles
{
    [Fact(DisplayName = "A plugin's biome registers after vanilla's, keeps its id on reload and is sent with data")]
    public void AddsPluginBiomes()
    {
        const string json = """
            {
              "has_precipitation": true,
              "temperature": 0.5,
              "downfall": 1,
              "effects": { "water_color": 4159204, "fog_color": "#c0d8ff" },
              "spawners": { "monster": [{ "type": "minecraft:zombie", "weight": 100, "minCount": 1, "maxCount": 4 }] },
              "spawn_costs": {},
              "carvers": ["minecraft:cave"],
              "features": [[], "#minecraft:example"]
            }
            """;

        var vanillaCount = CodecRegistry.Biomes.Table.VanillaCount;

        // Vanilla's entries come from the pack clients share with the server, so plugins can't override them.
        Assert.Throws<InvalidDataException>(() => Parse("minecraft:plains", json));

        PluginCodecs.Apply([Parse("codecfiles:glade", json)], NullLogger.Instance);

        Assert.True(CodecRegistry.TryGetBiome("codecfiles:glade", out var glade));
        Assert.True(glade!.Id >= vanillaCount);
        Assert.Same(glade, CodecRegistry.GetBiome(glade.Id));
        Assert.Equal(0.5f, glade.Element.Temperature);

        // Reloading with other data keeps the entry and its id, and tells clients to configure again.
        var revision = PluginCodecs.Revision;
        PluginCodecs.Apply([Parse("codecfiles:glade", json.Replace("0.5", "0.25"))], NullLogger.Instance);

        Assert.Same(glade, CodecRegistry.Biomes.All["codecfiles:glade"]);
        Assert.Equal(0.25f, glade.Element.Temperature);
        Assert.True(PluginCodecs.Revision > revision);

        // Vanilla's biomes are sent by name, the plugin's with its file as NBT: numbers as doubles, booleans as bytes.
        using var buffer = new NetworkBuffer();
        new RegistryDataPacket(CodecRegistry.Biomes.CodecKey, CodecRegistry.Biomes.Table.NetworkEntries)
            .Serialize(buffer);

        using var reader = new NetworkBuffer(buffer.GetBuffer()[..buffer.Size]);
        Assert.Equal(CodecRegistry.Biomes.CodecKey, reader.ReadString());

        var count = reader.ReadVarInt();
        NbtCompound? data = null;

        for (var id = 0; id < count; id++)
        {
            var name = reader.ReadString();
            var hasData = reader.ReadBoolean();
            Assert.Equal(id >= vanillaCount, hasData);

            if (!hasData)
                continue;

            var entry = reader.ReadNbtCompound();
            if (name == glade.Name)
            {
                Assert.Equal(glade.Id, id);
                data = entry;
            }
        }

        Assert.NotNull(data);
        Assert.Equal(0.25, Assert.IsType<NbtTag<double>>(data["temperature"]).Value);
        Assert.Equal(1.0, Assert.IsType<NbtTag<double>>(data["downfall"]).Value);
        Assert.Equal((byte)1, Assert.IsType<NbtTag<byte>>(data["has_precipitation"]).Value);
        Assert.Equal("#c0d8ff", Assert.IsType<NbtCompound>(data["effects"]).GetString("fog_color"));
    }

    private static CodecFile Parse(string identifier, string json) =>
        PluginCodecs.Parse(PluginCodecs.Biome, identifier, "test", Encoding.UTF8.GetBytes(json));
}
