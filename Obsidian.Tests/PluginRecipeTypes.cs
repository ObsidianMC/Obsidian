using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Plugins;
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
                ["data/probe/recipe/press.json"] = Encoding.UTF8.GetBytes("""
                    { "type": "probe:press", "ingredient": "minecraft:bedrock", "result": "minecraft:diamond" }
                    """),
                ["data/probe/recipe/baked.json"] = Encoding.UTF8.GetBytes("""
                    {
                      "type": "minecraft:smelting", "ingredient": "minecraft:bedrock",
                      "result": { "id": "minecraft:diamond" }, "cookingtime": 4,
                      "experience": 1.0, "category": "misc"
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
        Assert.DoesNotContain("probe:press", set.UpdatePacket.Recipes.Keys);
        var furnace = Assert.IsType<FurnaceBlockEntity>(BlockEntityNbt.Create("minecraft:furnace", default));
        furnace.Inventory[0] = new ItemStack(ItemsRegistry.Bedrock, 1);
        furnace.Inventory[1] = new ItemStack(ItemsRegistry.Coal, 1);
        for (var tick = 0; tick < 4; tick++)
            furnace.Cook(set);

        Assert.True(furnace.Inventory[2]?.Is(ItemsRegistry.Diamond));
        var weak = new WeakReference(plugin.LoadContext);
        await manager.UnloadPluginAsync(plugin);
        // Drop the immutable snapshot and returned plugin values before the caller checks collection.
        set = null;
        result = null;
        registered = null;
        plugin = null;
        return (id, weak);
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
