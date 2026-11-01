using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Structures.Pools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Trait("Category", TestCategory.Worldgen)]
[Collection(nameof(PluginTagFiles))]
public sealed class PluginStructureRegistry
{
    [Fact]
    public void MalformedProcessorDataLeavesOtherPluginsPoolsAvailable()
    {
        var bad = Pool("broken:pool", "broken plugin's pool", """
            { "element_type": "minecraft:single_pool_element", "location": "broken:template",
              "projection": "rigid", "processors": { "processors": [{ "processor_type": 42 }] } }
            """);
        var good = Pool("healthy:pool", "healthy plugin's pool", """
            { "element_type": "minecraft:empty_pool_element" }
            """);
        var logger = new PluginFeatureFiles.ListLogger();
        var features = new FeatureRegistry(WorldData.Generators.Density.WorldgenData.Vanilla);

        var structures = StructureData.Read(features, [bad, good], [], [], logger);

        Assert.Null(structures.Pool(bad.Identifier));
        Assert.NotNull(structures.Pool(good.Identifier));
        Assert.Single(logger.Messages, message => message.Contains(bad.Source, StringComparison.Ordinal));
    }

    [Fact]
    public void PoolElementsPlaceNativeInlineFeaturesAndKeepTheirDefinitions()
    {
        List<WorldgenTypeRegistration> types = [];
        types.Add(WorldgenTypeRegistration.Create(
            types, "probe:holder", typeof(ConfiguredFeatureBase), typeof(HolderFeature)));
        var features = PluginFeatures.BuildRegistry([], [], types, NullLogger.Instance);
        var definitions = new[]
        {
            """{ "type": "minecraft:simple_block", "to_place": { "id": "minecraft:emerald_block" } }""",
            """
            { "type": "minecraft:random_boolean_selector",
              "feature_true": { "feature": {
                "type": "minecraft:simple_block", "to_place": { "id": "minecraft:emerald_block" } } },
              "feature_false": { "feature": {
                "type": "minecraft:simple_block", "to_place": { "id": "minecraft:emerald_block" } } } }
            """,
            """
            { "type": "probe:holder", "child": {
              "type": "minecraft:simple_block", "to_place": { "id": "minecraft:emerald_block" } } }
            """
        };

        foreach (var definition in definitions)
        {
            var file = Pool("probe:inline", "probe plugin's pool", $$"""
                { "element_type": "minecraft:feature_pool_element", "projection": "rigid",
                  "feature": { "feature": {{definition}}, "placement": [] } }
                """);
            var logger = new PluginFeatureFiles.ListLogger();
            var structures = StructureData.Read(features, [file], [], types, logger);
            Assert.Empty(logger.Messages);
            var pool = structures.Pools[file.Identifier];
            var element = Assert.IsType<FeaturePoolElement>(Assert.Single(pool.Elements).Element);
            var context = PluginFeatureRegistry.Context(PluginFeatureRegistry.Region());

            Assert.True(element.Feature.Place(context.Level, context.Generation, context.Random, context.Origin));
            Assert.True(context.Level.GetBlock(context.Origin).Is(BlockIds.EmeraldBlock));
            Assert.True(element.Save(null).TryGetTag<Obsidian.Nbt.NbtCompound>("feature", out _));
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task NativeGenerationAndStaleStructureReadsLetUnloadedPluginsBeCollected()
    {
        await using var context = TestServerFactory.CreateServer($"native-unload-{Guid.NewGuid():N}");
        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
        var directory = Directory.CreateTempSubdirectory("obsidian-native-unload-");
        try
        {
            var path = Path.Join(directory.FullName, "probe.obby");
            await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
            var loadContext = await GenerateAndUnloadAsync(manager, path);

            for (var i = 0; loadContext.IsAlive && i < 100; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(20);
            }

            Assert.False(loadContext.IsAlive, "The unloaded native worldgen plugin is still referenced.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // Keep every strong reference in a completed helper, as the other collectible-plugin tests do.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> GenerateAndUnloadAsync(PluginManager manager, string path)
    {
        var plugin = (await manager.LoadPluginAsync(path))!;
        Assert.True(plugin.LoadContext.IsCollectible);
        var weak = new WeakReference(plugin.LoadContext);
        var features = Generate(plugin);

        await manager.UnloadPluginAsync(plugin);
        Assert.DoesNotContain(manager.Plugins, candidate => ReferenceEquals(candidate, plugin));

        // Unload has published fresh structure inputs. An in-flight reader now supplies its old feature registry,
        // including registered plugin types not needed by the new structures. The current cache must not pin it.
        ReadStale(features);
        features = null!;
        plugin = null!;
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FeatureRegistry Generate(PluginContainer plugin)
    {
        var modifier = PluginFeatures.Parse(
            PluginFeatures.BiomeModifier,
            "probe:native_decoration",
            "probe plugin",
            """
            { "biomes": "minecraft:plains", "add": { "features": { "underground_ores": "probe:pillar" } } }
            """u8);
        var features = PluginFeatures.BuildRegistry(
            [.. plugin.FeatureFiles, modifier], [], plugin.WorldgenTypes, NullLogger.Instance,
            PluginFeatureRegistry.EmptyBiomes());
        var region = PluginFeatureRegistry.Region(out var chunk);
        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var x = 0; x < 16; x += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        new BiomeDecoration(features, ["minecraft:plains"]).Generate(region, chunk, [chunk], -64, 384);
        Assert.True(PluginFeatureFiles.HasDiamondBlocks(chunk));
        return features;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadStale(FeatureRegistry features) => StructureData.Read(features);

    private static StructureFile Pool(string id, string source, string element) => PluginStructures.Parse(
        PluginStructures.TemplatePool,
        id,
        source,
        Encoding.UTF8.GetBytes($$"""
            { "fallback": "minecraft:empty", "elements": [{ "weight": 1, "element": {{element}} }] }
            """));

    public sealed class HolderFeature : ConfiguredFeatureBase
    {
        public override string Type => "probe:holder";

        public required ConfiguredFeatureBase Child { get; init; }

        public override bool Place(FeatureContext context) => this.Child.Place(context);
    }
}
