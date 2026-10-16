using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
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

[Collection(nameof(PluginTagFiles))]
public sealed class PluginRecipeTypes
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FileRecipesMatchOnlyWhileTheirPluginIsLoadedAndReleaseItsSerializer()
    {
        await using var context = TestServerFactory.CreateServer($"recipe-types-{Guid.NewGuid():N}");
        await using var other = TestServerFactory.CreateServer($"other-recipes-{Guid.NewGuid():N}");
        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
        var directory = Path.Join(Path.GetTempPath(), $"obsidian-recipe-types-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "probe.obby");
        try
        {
            var files = new Dictionary<string, byte[]>
            {
                ["data/probe/tags/item/materials.json"] = Encoding.UTF8.GetBytes("""
                    { "values": ["minecraft:bedrock"] }
                    """),
                ["data/c/tags/item/logs.json"] = Encoding.UTF8.GetBytes("""
                    { "values": ["minecraft:barrier"] }
                    """),
                ["data/probe/recipe/press.json"] = Encoding.UTF8.GetBytes("""
                    { "type": "probe:press", "ingredient": "minecraft:bedrock", "result": "minecraft:diamond" }
                    """),
                ["data/probe/recipe/baked.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:smelting", "ingredient": "#probe:materials",
                      "result": { "id": "minecraft:diamond" }, "cookingtime": 4,
                      "experience": 1.0, "category": "misc"
                    }
                    """),
                ["data/probe/recipe/tagged.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:crafting_shapeless", "category": "misc",
                      "ingredients": ["#c:ingots/iron", ["#probe:materials"]],
                      "result": { "id": "minecraft:diamond" }
                    }
                    """),
                ["data/probe/recipe/cut.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:stonecutting", "ingredient": { "tag": "c:logs" },
                      "result": { "id": "minecraft:diamond" }
                    }
                    """),
                ["data/probe/recipe/smith.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:smithing_transform", "template": "#c:ingots/iron",
                      "base": "#c:logs", "addition": "#probe:materials",
                      "result": { "id": "minecraft:diamond" }
                    }
                    """),
                ["data/probe/recipe/unqualified.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:stonecutting", "ingredient": "#logs",
                      "result": { "id": "minecraft:diamond" }
                    }
                    """),
                ["data/probe/recipe/missing.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:crafting_shapeless", "category": "misc",
                      "ingredients": [["minecraft:bedrock", "#missing:logs"]],
                      "result": { "id": "minecraft:diamond" }
                    }
                    """)
            };
            files["data/probe/recipe/unregistered.json"] = Encoding.UTF8.GetBytes("""
                {
                  "type": "probe:smelting", "ingredient": "minecraft:bedrock",
                  "result": { "id": "minecraft:diamond" }, "cookingtime": 4,
                  "experience": 1.0, "category": "misc"
                }
                """);
            await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));
            var (id, unloaded) = await LoadMatchAndUnload(manager, context.Server, other.Server, path);
            var token = new RecipeType<object>("probe:press", id);
            Assert.Null(context.Server.FindRecipe(token, [new ItemStack(ItemsRegistry.Bedrock, 1)]));
            Assert.Null(context.Server.RecipeRegistry.Types.Get("probe:press"));
            Assert.DoesNotContain("probe:baked", context.Server.RecipeRegistry.Current.Recipes.Keys);

            // The shared identity is only strings and integers, so it cannot retain the plugin assembly.
            for (var attempt = 0; unloaded.IsAlive && attempt < 100; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(20);
            }

            Assert.False(unloaded.IsAlive);
            var (reloadedId, _) = await LoadMatchAndUnload(manager, context.Server, other.Server, path);
            Assert.Equal(id, reloadedId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(int Id, WeakReference Context)> LoadMatchAndUnload(
        PluginManager manager, Server server, Server other, string path)
    {
        var plugin = await manager.LoadPluginAsync(path);
        Assert.NotNull(plugin);
        var registered = plugin.Plugin!.GetType().GetProperty(nameof(ProbePlugin.PressType))!.GetValue(plugin.Plugin)!;
        var id = (int)registered.GetType().GetProperty("Id")!.GetValue(registered)!;
        var token = new RecipeType<object>("probe:press", id);
        var result = server.FindRecipe(token, [new ItemStack(ItemsRegistry.Bedrock, 1)]);
        Assert.NotNull(result);
        Assert.Equal("probe:press", result.GetType().GetProperty("Identifier")!.GetValue(result));
        Assert.Null(server.FindRecipe(token, [new ItemStack(ItemsRegistry.Stone, 1)]));
        Assert.Null(other.FindRecipe(token, [new ItemStack(ItemsRegistry.Bedrock, 1)]));
        var set = server.RecipeRegistry.Current;
        Assert.Contains("probe:baked", set.Recipes.Keys);
        Assert.DoesNotContain("probe:unregistered", set.Recipes.Keys);
        Assert.DoesNotContain("probe:missing", set.Recipes.Keys);
        Assert.DoesNotContain("probe:press", set.UpdatePacket.Recipes.Keys);
        AssertTaggedRecipes(set, ItemsRegistry.Bedrock);
        var furnace = Assert.IsType<FurnaceBlockEntity>(BlockEntityNbt.Create("minecraft:furnace", default));
        furnace.Inventory[0] = new ItemStack(ItemsRegistry.Bedrock, 1);
        furnace.Inventory[1] = new ItemStack(ItemsRegistry.Coal, 1);
        for (var tick = 0; tick < 4; tick++)
            furnace.Cook(set);

        Assert.True(furnace.Inventory[2]?.Is(ItemsRegistry.Diamond));

        // Rebind the same recipe files after a tag change, as the plugin manager does on load and unload.
        var changed = PluginTags.Parse("item", "probe:materials", "reload",
            Encoding.UTF8.GetBytes("""{ "replace": true, "values": ["minecraft:structure_block"] }"""));
        PluginTags.Apply([.. plugin.TagFiles, changed], NullLogger.Instance);
        server.LoadRecipeFiles([(plugin, plugin.RecipeFiles)]);
        AssertTaggedRecipes(server.RecipeRegistry.Current, ItemsRegistry.StructureBlock);
        Assert.False(server.RecipeRegistry.Current.IsCookingIngredient(
            CraftingType.Smelting, new ItemStack(ItemsRegistry.Bedrock, 1)));
        AssertTaggedRecipes(set, ItemsRegistry.Bedrock);

        var weak = new WeakReference(plugin.LoadContext);
        await manager.UnloadPluginAsync(plugin);
        // Drop the immutable snapshot and returned plugin values before the caller checks collection.
        set = null;
        result = null;
        registered = null;
        plugin = null;
        return (id, weak);
    }

    private static void AssertTaggedRecipes(RecipeSet set, Item material)
    {
        ItemStack?[] grid = [new(ItemsRegistry.IronIngot, 1), new(material, 1), null, null];
        var match = Assert.IsType<CraftingEvaluation>(set.EvaluateCrafting(grid, 2));
        Assert.Equal("probe:tagged", match.RecipeIdentifier);
        Assert.True(match.Output.Is(ItemsRegistry.Diamond));
        Assert.True(set.IsCookingIngredient(CraftingType.Smelting, new ItemStack(material, 1)));

        var cut = Assert.IsType<CuttingRecipe>(set.Recipes["probe:cut"]);
        Assert.Equal(ItemsRegistry.Barrier, Assert.Single(cut.Ingredient).Holder);
        Assert.DoesNotContain(cut.Ingredient, stack => stack.Is(ItemsRegistry.OakLog));
        var logs = Assert.IsType<CuttingRecipe>(set.Recipes["probe:unqualified"]);
        Assert.Contains(logs.Ingredient, stack => stack.Is(ItemsRegistry.OakLog));

        // Check the actual packet, including filters used by cooking and smithing screens.
        var smith = set.Recipes["probe:smith"];
        var cooking = set.Recipes["probe:baked"];
        var packet = new UpdateRecipesPacket(new Dictionary<string, IRecipe>
        {
            [cut.Identifier] = cut,
            [smith.Identifier] = smith,
            [cooking.Identifier] = cooking
        });
        using var writer = new NetworkBuffer();
        packet.Serialize(writer);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        Assert.Equal(7, reader.ReadVarInt());
        var properties = new Dictionary<string, int[]>();
        for (var i = 0; i < 7; i++)
        {
            var name = reader.ReadString();
            properties[name] = Enumerable.Range(0, reader.ReadVarInt()).Select(_ => reader.ReadVarInt()).ToArray();
        }

        Assert.Equal([(int)material.Id], properties["minecraft:furnace_input"]);
        Assert.Equal([(int)material.Id], properties["minecraft:smithing_addition"]);
        Assert.Equal([(int)ItemsRegistry.Barrier.Id], properties["minecraft:smithing_base"]);
        Assert.Equal([(int)ItemsRegistry.IronIngot.Id], properties["minecraft:smithing_template"]);
        Assert.Equal(1, reader.ReadVarInt());
        Assert.Equal(2, reader.ReadVarInt());
        Assert.Equal(ItemsRegistry.Barrier.Id, reader.ReadVarInt());
    }
}

/// <summary>Recipe values loaded in the probe's collectible assembly, not in the host's copy of the tests.</summary>
public sealed record ProbePressRecipe(string Identifier, Item Input, Item Result);

public sealed class ProbePressSerializer : IRecipeSerializer<ProbePressRecipe>
{
    public ProbePressRecipe Read(string identifier, JsonElement json) => new(
        identifier,
        ItemsRegistry.Get(json.GetProperty("ingredient").GetString()!),
        ItemsRegistry.Get(json.GetProperty("result").GetString()!));

    public bool Matches(ProbePressRecipe recipe, IReadOnlyList<ItemStack?> inputs) =>
        inputs.Count == 1 && inputs[0] is { Count: > 0 } stack && stack.Holder == recipe.Input;
}
