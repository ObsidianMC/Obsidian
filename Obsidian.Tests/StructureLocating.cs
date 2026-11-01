using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Commands.Modules;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Finding structures, vanilla's and plugins', with <c>/locate</c> and eyes of ender.
/// </summary>
/// <remarks>These tests run with the other tests applying plugins' tags and structures.</remarks>
[Collection(nameof(PluginTagFiles))]
public class StructureLocating
{
    [Fact(DisplayName = "/locate structure finds vanilla's structures where vanilla does, by identifier and tag")]
    public void LocatesVanillaStructures()
    {
        // Vanilla's plains village starts in chunk (-9, -52) of seed 12345, the only
        // start chunk of the villages' placement region around the origin, which vanilla's search looks at first.
        var structures = PluginStructureTypes.Only(
            Obsidian.WorldData.Generators.Features.StructureData.Read(
                new Obsidian.WorldData.Generators.Features.FeatureRegistry(
                    Obsidian.WorldData.Generators.Density.WorldgenData.Vanilla)), "minecraft:villages");
        var manager = NativeChunks.Pipeline(12345L, () => structures).Structures!;
        var origin = new Vector(-100, 70, -800);

        var found = Locate("minecraft:village_plains");
        Assert.Equal("commands.locate.structure.success", found.Translate);
        Assert.Equal("minecraft:village_plains", found.With![0].Text);

        // The position's height isn't known, and the distance is horizontal.
        var link = found.With[1];
        Assert.Equal("chat.square_brackets", link.Translate);
        Assert.Equal(["-144", "~", "-832"], link.With![0].With!.Select(argument => argument.Text));
        Assert.Equal("/tp @s -144 ~ -832", link.ClickEvent!.Value);
        Assert.Equal("54", found.With[2].Text);

        // A tag names the structure found.
        Assert.Equal("#minecraft:village (minecraft:village_plains)", Locate("#village").With![0].Text);

        Assert.Equal("commands.locate.structure.not_found", Locate("minecraft:mansion").Translate);
        Assert.Equal("commands.locate.structure.invalid", Locate("minecraft:missing").Translate);
        Assert.Equal("commands.locate.structure.invalid", Locate("#minecraft:missing").Translate);

        ChatMessage Locate(string argument) => LocateCommandModule.LocateStructure(
            argument,
            origin,
            structures.Structures,
            targets => new StructureLocator(manager).Locate(targets, origin, LocateCommandModule.SearchRadius, false));
    }

    [Fact(DisplayName = "Eyes of ender and /locate find a plugin's structures, through the eyes' tag")]
    public void LocatesPluginStructures()
    {
        var built = Obsidian.WorldData.Generators.Features.StructureData.Read(
            new Obsidian.WorldData.Generators.Features.FeatureRegistry(
                Obsidian.WorldData.Generators.Density.WorldgenData.Vanilla),
            PluginStructureTypes.SpireFiles(),
            [],
            PluginStructureTypes.Types(),
            NullLogger.Instance);

        var structures = PluginStructureTypes.Only(built, "structuretest:spires");
        var seed = PluginStructureTypes.Seed;
        var builder = NativeChunks.Pipeline(seed, () => structures);
        var manager = builder.Structures!;
        var origin = new Vector(40, 64, 40);

        var located = LocateCommandModule.LocateStructure(
            PluginStructureTypes.Spire,
            origin,
            structures.Structures,
            targets => new StructureLocator(manager).Locate(targets, origin, LocateCommandModule.SearchRadius, false));

        Assert.Equal(["32", "~", "32"], located.With![1].With![0].With!.Select(argument => argument.Text));

        try
        {
            // The plugin adds its structure to the eyes' tag, besides vanilla's strongholds.
            var tag = PluginTags.Parse(
                PluginTags.Structure,
                EyeOfEnder.LocatedTag,
                "test",
                Encoding.UTF8.GetBytes("""{ "values": ["structuretest:spire"] }"""));

            PluginTags.Apply([tag], NullLogger.Instance, [(PluginTags.Structure, PluginStructureTypes.Spire)]);

            var target = new StructureLocator(manager)
                .Locate(EyeOfEnder.LocatedTag, origin, LocateCommandModule.SearchRadius, false);

            Assert.Equal(PluginStructureTypes.Spire, target!.Value.Structure.Identifier);
            Assert.Equal(new Vector(32, 0, 32), target.Value.Position);

            // Without it, the tag's strongholds aren't in this level's structure sets: eyes find nothing.
            PluginTags.Apply([], NullLogger.Instance);

            Assert.Null(new StructureLocator(manager)
                .Locate(EyeOfEnder.LocatedTag, origin, LocateCommandModule.SearchRadius, false));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }

        // An eye flies at most 12 blocks toward a farther structure, rising 8 blocks, like vanilla's.
        var from = new VectorD(0.5, 64, 0.5);
        Assert.Equal(new VectorD(12.5, 72, 0.5), EyeOfEnder.FlightTarget(from, new VectorD(100, 0, 0.5)));
        Assert.Equal(new VectorD(5, 0, 5), EyeOfEnder.FlightTarget(from, new VectorD(5, 0, 5)));
    }

    [Fact(DisplayName = "Eyes of ender are saved with their item, like vanilla's")]
    public void SavesEyesOfEnder()
    {
        var eye = new EyeOfEnder
        {
            Level = null!,
            Position = new VectorD(10.5, 70, -3.25),
            Motion = new VectorD(0.1, 0.05, -0.2)
        };

        eye.SignalTo(new VectorD(100, 0, 0));

        var saved = EntityNbt.Save(eye)!;
        Assert.Equal("minecraft:eye_of_ender", saved.GetString("id"));
        Assert.True(saved.TryGetTag<NbtCompound>("Item", out var item));
        Assert.Equal("minecraft:ender_eye", item.GetString("id"));

        var loaded = Assert.IsType<EyeOfEnder>(EntityNbt.Load(saved, null!));
        Assert.Equal(eye.Position, loaded.Position);
        Assert.Equal(eye.Motion, loaded.Motion);
        Assert.True(loaded.Item!.Is(ItemsRegistry.EnderEye));

        // Saved without its item, it gets an eye of ender, as vanilla's does.
        saved.Remove("Item");
        loaded = Assert.IsType<EyeOfEnder>(EntityNbt.Load(saved, null!));
        Assert.True(loaded.Item!.Is(ItemsRegistry.EnderEye));
    }
}
