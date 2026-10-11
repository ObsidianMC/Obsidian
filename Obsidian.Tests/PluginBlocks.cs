using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Registries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Blocks plugins register: their states' ids, which clients work out the same way, and how the server treats them.
/// </summary>
public class PluginBlocks
{
    [Fact(DisplayName = "Block states are numbered like vanilla numbers them, so clients agree on plugins' states")]
    public void StatesAreNumberedLikeVanilla()
    {
        using var stream = typeof(BlocksRegistry).Assembly.GetManifestResourceStream("Obsidian.Assets.blocks.json")!;
        using var report = JsonDocument.Parse(stream);

        foreach (var block in report.RootElement.EnumerateObject())
        {
            if (!block.Value.TryGetProperty("properties", out var propertiesElement))
                continue;

            var properties = propertiesElement.EnumerateObject()
                .Select(property => (property.Name, Values: property.Value.EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToList()))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToList();

            var counts = properties.Select(property => property.Values.Count).ToList();
            var states = block.Value.GetProperty("states").EnumerateArray().ToList();
            var firstId = states.Min(state => state.GetProperty("id").GetInt32());

            foreach (var state in states)
            {
                var values = state.GetProperty("properties");
                var indexes = properties
                    .Select(property => property.Values.IndexOf(values.GetProperty(property.Name).GetString()!))
                    .ToList();

                Assert.Equal(state.GetProperty("id").GetInt32() - firstId, BlockType.StateIndex(counts, indexes));
            }
        }

        // The property kinds list their values in the order vanilla's do.
        string[] Values(string block, string property) => report.RootElement
            .GetProperty(block)
            .GetProperty("properties")
            .GetProperty(property)
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        Assert.Equal(Values("minecraft:redstone_lamp", "lit"), StateProperty.Bool("lit").Values);
        Assert.Equal(Values("minecraft:wheat", "age"), StateProperty.Int("age", 0, 7).Values);
    }

    [Fact(DisplayName = "A plugin's block gets states after vanilla's, which the server reads and saves by name")]
    public void PluginBlockStates()
    {
        var properties = new BlockProperties
        {
            StateProperties = [StateProperty.Int("power", 0, 3), StateProperty.Bool("lit")],
            DefaultState = new Dictionary<string, string> { ["lit"] = "false" },
            LightEmission = 7,
            Hardness = 3
        };

        var lamp = BlocksRegistry.Register("blocks:lamp", properties);
        Assert.True(BlocksRegistry.TryBind(lamp, properties, new FakePluginContainer(), () => false));

        // After every vanilla state, numbered by the properties sorted by name: lit, then power.
        Assert.True(lamp.FirstStateId >= BlocksRegistry.StateToNumeric.Length);
        Assert.Equal(8, lamp.StateCount);
        Assert.Equal(["lit", "power"], lamp.Properties.Select(property => property.Name));
        Assert.Equal(lamp.FirstStateId + 4, lamp.DefaultState.GetHashCode());
        Assert.Equal(
            (int)Math.Ceiling(Math.Log2(BlocksRegistry.StateCount)),
            BlocksRegistry.GlobalBitsPerBlocks);

        // The server finds the states by id, and by name and properties, which is how chunks are saved and loaded.
        var lit = lamp.With(lamp.DefaultState, "lit", "true");
        var state = BlocksRegistry.Get(lamp.FirstStateId + 2);

        Assert.True(lamp.Is(state));
        Assert.Equal(lamp.Id, BlocksRegistry.RegistryIdOf(state.GetHashCode()));
        Assert.Equal("blocks:lamp", state.UnlocalizedName);
        Assert.Equal(new Dictionary<string, string> { ["lit"] = "true", ["power"] = "2" }, state.GetProperties());
        Assert.Equal(state, lit.WithProperty("power", 2));
        Assert.Equal(state, state.WithProperty("power", "9"));
        Assert.True(BlockStateProperties.TryGetState(
            "blocks:lamp",
            new Dictionary<string, string> { ["power"] = "2", ["lit"] = "true" },
            out var loaded));
        Assert.Equal(state, loaded);

        // A solid block like stone, with the plugin's light and hardness.
        Assert.False(state.IsAir);
        Assert.True(state.BlocksMotion());
        Assert.False(state.CanBeReplaced());
        Assert.Equal(7, state.LightEmission());
        Assert.Equal(3, BlockPhysics.MiningHardness(state.GetHashCode()));
        Assert.NotEmpty(Entities.AI.BlockCollisionShapes.Get(state));

        // Registering it again, as a reload does, keeps its ids; its states can't change until the server restarts.
        Assert.Same(lamp, BlocksRegistry.Register("blocks:lamp", properties));
        Assert.Throws<ArgumentException>(() => BlocksRegistry.Register("blocks:lamp", new BlockProperties()));
        Assert.Throws<ArgumentException>(() => BlocksRegistry.Register("minecraft:stone", new BlockProperties()));

        // However many properties a block has, it can't have more states than the limit.
        var tooMany = new BlockProperties
        {
            StateProperties = [.. Enumerable.Range(0, 31).Select(i => StateProperty.Bool($"flag_{i}"))]
        };
        Assert.Throws<ArgumentException>(() => BlocksRegistry.Register("blocks:too_many", tooMany));
    }

    private sealed class FakePluginContainer : API.Plugins.IPluginContainer
    {
        public API.Plugins.PluginBase Plugin => null!;
        public Microsoft.Extensions.DependencyInjection.IServiceScope ServiceScope => null!;
        public Assembly PluginAssembly => typeof(PluginBlocks).Assembly;

        public void InjectServices(Microsoft.Extensions.Logging.ILogger? logger, object? module) { }

        public byte[]? GetFileData(string fileName) => null;
    }
}
