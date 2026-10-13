using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Crafting.Builders;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Recipes plugins register work like vanilla's: crafting grids match them, players' recipe books show them, and
/// they're gone, from both, when the plugin unloads.
/// </summary>
/// <remarks>
/// The recipes are the process's, so the tests' recipes take ingredients no vanilla recipe or other test uses
/// (bedrock and barriers), under identifiers of their own.
/// </remarks>
public sealed class PluginRecipes
{
    [Fact(DisplayName = "A plugin's recipe crafts in the player's grid and leaves it and their book on unload",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task RecipeCraftsUntilItsPluginUnloads()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-recipes-{Guid.NewGuid():N}");
        var server = context.Server;
        var player = await JoinAsync(context, "Crafter");
        var client = (TestClient)player.Client;
        var plugin = Plugin();

        // The ingredients are in the 2x2 grid before the recipe exists.
        player.Inventory.SetItem(1, new ItemStack(ItemsRegistry.Bedrock, 1));
        player.Inventory.SetItem(2, new ItemStack(ItemsRegistry.Barrier, 1));

        var recipe = ShapelessRecipeBuilder.Create(CraftingBookCategory.Misc)
            .AddIngredients(IngredientOf(ItemsRegistry.Bedrock), IngredientOf(ItemsRegistry.Barrier))
            .HasNoGroup()
            .WithIdentifier($"probe:shapeless_{Guid.NewGuid():N}")
            .WithResult(new ItemStack(ItemsRegistry.Diamond, 3))
            .Build();

        try
        {
            server.RegisterRecipes([recipe], plugin);
            await server.SyncRecipesAsync();

            // The open grid shows the result now, and the player's book has the recipe.
            Assert.True(player.Inventory.GetItem(0)?.Is(ItemsRegistry.Diamond));
            var added = Assert.Single(client.SentPackets.OfType<RecipeBookAddPacket>(), packet => !packet.Replace);
            var entry = Assert.Single(added.Entries);
            Assert.Same(recipe, entry.Recipe);

            // Taking the result crafts it.
            await Click(0, 0, ClickType.Pickup).HandleAsync(server, player);
            Assert.True(player.CarriedItem?.Is(ItemsRegistry.Diamond));
            Assert.Equal(3, player.CarriedItem?.Count);
            Assert.Null(player.Inventory.GetItem(1));

            player.Inventory.SetItem(1, new ItemStack(ItemsRegistry.Bedrock, 1));
            player.Inventory.SetItem(2, new ItemStack(ItemsRegistry.Barrier, 1));
            await MainEventHandler.RefreshCraftingResultAsync(player);
            Assert.NotNull(player.Inventory.GetItem(0));

            plugin.UndoHostAdditions();
            await server.SyncRecipesAsync();

            // The result is gone, the client was told, and the recipe no longer matches.
            Assert.Null(player.Inventory.GetItem(0));
            Assert.Null(client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]);
            var removed = Assert.Single(client.SentPackets.OfType<RecipeBookRemovePacket>());
            Assert.Equal(new[] { entry.DisplayId }, removed.DisplayIds);
            Assert.Null(RecipesRegistry.FindRecipe(player.Inventory, 2));
        }
        finally
        {
            plugin.UndoHostAdditions();
        }
    }

    [Fact(DisplayName = "A plugin's recipe is in a joining player's book, fills a crafting table and keeps its id",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task RecipeBookPlacesAPluginRecipe()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-recipe-book-{Guid.NewGuid():N}");
        var server = context.Server;
        var identifier = $"probe:shaped_{Guid.NewGuid():N}";
        var plugin = Plugin();
        var reloaded = Plugin();

        try
        {
            var recipe = Shaped(identifier);
            server.RegisterRecipes([recipe], plugin);

            var player = await JoinAsync(context, "Placer");
            var client = (TestClient)player.Client;
            var book = Assert.Single(client.SentPackets.OfType<RecipeBookAddPacket>());
            Assert.True(book.Replace);
            var entry = Assert.Single(book.Entries, candidate => candidate.Recipe == recipe);

            // Placing it from the book moves the ingredients into the table's grid, centred, and shows the result.
            var table = new CraftingTable();
            player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Bedrock, 4));
            await player.OpenInventoryAsync(table);
            await MainEventHandler.PlaceBookRecipeAsync(
                player,
                player.CurrentContainerId,
                entry.DisplayId,
                makeAll: false);

            Assert.All(new[] { 1, 2, 4, 5 }, slot => Assert.True(table.GetItem(slot)?.Is(ItemsRegistry.Bedrock)));
            Assert.True(table.GetItem(0)?.Is(ItemsRegistry.Diamond));

            // Unloading the plugin clears the result; the same recipe registered again, as a reload does, gets its
            // id back and the result returns.
            plugin.UndoHostAdditions();
            await server.SyncRecipesAsync();
            Assert.Null(table.GetItem(0));

            server.RegisterRecipes([Shaped(identifier)], reloaded);
            await server.SyncRecipesAsync();

            var readded = client.SentPackets.OfType<RecipeBookAddPacket>().Last();
            Assert.False(readded.Replace);
            Assert.Equal(entry.DisplayId, Assert.Single(readded.Entries).DisplayId);
            Assert.True(table.GetItem(0)?.Is(ItemsRegistry.Diamond));
        }
        finally
        {
            plugin.UndoHostAdditions();
            reloaded.UndoHostAdditions();
        }
    }

    // A player who joined and got their recipe book, as the join does.
    private static async Task<Entities.Player> JoinAsync(RealServerContext context, string username)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, username);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        await player.RecipeBookSync.SendInitialAsync(player);

        return player;
    }

    private static PluginContainer Plugin() => new()
    {
        Info = new PluginInfo("probe"),
        Archive = PluginArchive.Open(
            "probe.obby",
            PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("probe", "1.0.0", "Probe")
            }))
    };

    // Four bedrock in a square craft three diamonds.
    private static ShapedRecipe Shaped(string identifier) => ShapedRecipeBuilder.Create(CraftingBookCategory.Misc)
        .WithKey('B', new ItemStack(ItemsRegistry.Bedrock, 1))
        .WithPattern("BB", "BB")
        .HasNoGroup()
        .WithIdentifier(identifier)
        .WithResult(new ItemStack(ItemsRegistry.Diamond, 3))
        .Build();

    private static Ingredient IngredientOf(Item item) => [new ItemStack(item, 1)];

    private static ContainerClickPacket Click(short slot, sbyte button, ClickType type)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteShort(slot);
        writer.WriteByte(button);
        writer.WriteVarInt((int)type);
        writer.WriteVarInt(0);
        writer.WriteBoolean(false);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new ContainerClickPacket();
        packet.Populate(reader);

        return packet;
    }
}
