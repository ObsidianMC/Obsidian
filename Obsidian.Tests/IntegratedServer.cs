using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using Obsidian.Integrated;
using Obsidian.Nbt;
using Obsidian.WorldData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Tests that change the server's static paths, which run alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServerPathsCollection
{
    public const string Name = "Server paths";
}

public class IntegratedProtocolTests
{
    [Fact]
    public void ParsesCommandsAndFormatsEvents()
    {
        Assert.True(IntegratedProtocol.TryParseCommand("""@obsidian:{"command":"publish","port":25565,"gameMode":"creative","allowCommands":true}""",
            out var publish));
        Assert.Equal(new IntegratedCommand { Command = "publish", Port = 25565, GameMode = "creative", AllowCommands = true }, publish);

        Assert.True(IntegratedProtocol.TryParseCommand("""@obsidian:{"command":"save","flush":true}""", out var save));
        Assert.True(save.Flush);

        // Ordinary console commands, broken JSON and objects without a command aren't commands.
        Assert.False(IntegratedProtocol.TryParseCommand("say hello", out _));
        Assert.False(IntegratedProtocol.TryParseCommand("@obsidian:{\"command\":", out _));
        Assert.False(IntegratedProtocol.TryParseCommand("@obsidian:{}", out _));

        Assert.Equal("""@obsidian:{"event":"ready","port":51234}""", IntegratedProtocol.FormatEvent(IntegratedEvent.Ready(51234)));
        Assert.Equal("""@obsidian:{"event":"saving","autosave":true}""", IntegratedProtocol.FormatEvent(IntegratedEvent.Saving(true)));
        Assert.Equal("""@obsidian:{"event":"progress","stage":"generating","percent":42}""",
            IntegratedProtocol.FormatEvent(IntegratedEvent.Progress("generating", 42)));
        Assert.Equal("""@obsidian:{"event":"stats","tickMs":3.5}""", IntegratedProtocol.FormatEvent(IntegratedEvent.Stats(3.4567)));
    }

    [Fact]
    public void JoinSecretMustMatchExactly()
    {
        var session = new IntegratedSession(Options.Create(new IntegratedConfiguration
        {
            Enabled = true,
            LocalPlayerName = "Dev",
            JoinSecret = "s3cret-☃"
        }));

        Assert.True(session.VerifyJoinSecret(Encoding.UTF8.GetBytes("s3cret-☃")));
        Assert.False(session.VerifyJoinSecret(Encoding.UTF8.GetBytes("s3cret-x")));
        Assert.False(session.VerifyJoinSecret(Encoding.UTF8.GetBytes("s3cret")));
        Assert.False(session.VerifyJoinSecret([]));

        // The name is reserved without regard to case, like vanilla's names.
        Assert.True(session.IsLocalPlayer("dev"));
        Assert.False(session.IsLocalPlayer("Steve"));

        // Without a configured secret nobody can join as the local player.
        var unconfigured = new IntegratedSession(Options.Create(new IntegratedConfiguration { Enabled = true, LocalPlayerName = "Dev" }));
        Assert.False(unconfigured.VerifyJoinSecret([]));
    }
}

public class VanillaLevelDataTests
{
    /// <summary>
    /// Obsidian's own flat worlds open with its superflat generator; flat worlds with other layers, like vanilla's
    /// classic preset (2 dirt), are refused instead of silently getting different terrain.
    /// </summary>
    [Fact]
    public void OnlyFlatWorldsWithObsidiansLayersOpen()
    {
        var data = VanillaLevelData.Create(new NewWorldConfiguration { WorldType = "flat" }, "Flat", 1, "superflat");
        Assert.Equal("superflat", VanillaLevelData.GetGeneratorId(data));

        var worldGen = (NbtCompound)data["WorldGenSettings"];
        var overworld = (NbtCompound)((NbtCompound)worldGen["dimensions"])["minecraft:overworld"];
        var settings = (NbtCompound)((NbtCompound)overworld["generator"])["settings"];
        var dirt = (NbtCompound)((NbtList)settings["layers"])[1];

        dirt.Remove("height");
        dirt.Add(new NbtTag<int>("height", 2));

        Assert.Throws<NotSupportedException>(() => VanillaLevelData.GetGeneratorId(data));
    }

    /// <summary>
    /// A world made in vanilla with a data pack changing the overworld saves the pack's stem: it opens when the same
    /// stem overrides the overworld, read the way vanilla's codecs saved it (floats, climate pairs, short ids), and is
    /// refused with where it differs otherwise.
    /// </summary>
    [Theory]
    [InlineData(null, "isn't vanilla's default")]
    [InlineData("plains", null)]
    [InlineData("minecraft:desert", "differs at generator.biome_source.biomes[0].biome")]
    public void DataPackOverworldsOpenWithTheirStem(string? biome, string? error)
    {
        var parameters = new NbtCompound("parameters")
        {
            Pair("temperature", -1f, 1f),
            Pair("humidity", -1f, 1f),
            Pair("continentalness", -1.2f, 1f),
            Pair("erosion", -1f, 1f),
            Pair("depth", 0f, 0f),
            Pair("weirdness", -1f, 1f),
            new NbtTag<float>("offset", 0.1f)
        };

        var entry = new NbtCompound
        {
            new NbtTag<string>("biome", "minecraft:plains"),
            parameters
        };

        var data = VanillaLevelData.Create(new NewWorldConfiguration(), "Pack", 1, "minecraft:mojang_generator");
        var dimensions = (NbtCompound)((NbtCompound)data["WorldGenSettings"])["dimensions"];
        var generator = (NbtCompound)((NbtCompound)dimensions["minecraft:overworld"])["generator"];

        generator.Remove("biome_source");
        generator.Add(new NbtCompound("biome_source")
        {
            new NbtTag<string>("type", "minecraft:multi_noise"),
            new NbtList(NbtTagType.Compound, "biomes") { entry }
        });

        var stem = $$"""
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:overworld",
                "biome_source": {
                  "type": "minecraft:multi_noise",
                  "biomes": [
                    {
                      "biome": "{{biome}}",
                      "parameters": {
                        "temperature": [-1, 1], "humidity": [-1, 1], "continentalness": [-1.2, 1],
                        "erosion": [-1, 1], "depth": 0, "weirdness": [-1, 1], "offset": 0.1
                      }
                    }
                  ]
                }
              }
            }
            """;

        (string, byte[])? overworld = biome is null ? null : ("test pack", Encoding.UTF8.GetBytes(stem));

        if (error is null)
        {
            Assert.Equal("minecraft:mojang_generator", VanillaLevelData.GetGeneratorId(data, overworld));
            return;
        }

        var exception = Assert.Throws<NotSupportedException>(() => VanillaLevelData.GetGeneratorId(data, overworld));
        Assert.Contains(error, exception.Message);

        static NbtList Pair(string name, float min, float max) => new(NbtTagType.Float, name)
        {
            new NbtTag<float>("", min),
            new NbtTag<float>("", max)
        };
    }
}
