using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Crafting.Builders;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Recipes plugins register work like vanilla's: crafting grids match them, players' recipe books show them, and
/// they're gone, from both, when the plugin unloads.
/// </summary>
/// <remarks>
/// Plugin recipes belong to their server; test recipes use bedrock and barriers to avoid vanilla matches.
/// Content fingerprint checks share the process-wide registries with the plugin and tag loading tests.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public sealed class PluginRecipes
{
    [Fact]
    public async Task CraftingEvaluationOwnsItsStacksAndTracksRecipeChanges()
    {
        await using var context = TestServerFactory.CreateServer($"craft-evaluation-{Guid.NewGuid():N}");
        IServer server = context.Server;
        ItemStack?[] grid =
        [
            new(ItemsRegistry.MilkBucket, 1),
            new(ItemsRegistry.MilkBucket, 1),
            new(ItemsRegistry.MilkBucket, 1),
            new(ItemsRegistry.Sugar, 2),
            new(ItemsRegistry.Egg, 2),
            new(ItemsRegistry.Sugar, 2),
            new(ItemsRegistry.Wheat, 2),
            new(ItemsRegistry.Wheat, 2),
            new(ItemsRegistry.Wheat, 2)
        ];
        var result = Assert.IsType<CraftingEvaluation>(server.EvaluateCrafting(grid, 3));
        Assert.Equal("cake", result.RecipeIdentifier);
        Assert.True(result.Output.Is(ItemsRegistry.Cake));
        Assert.Equal(1, result.Output.Count);
        Assert.All(result.ConsumedCounts, count => Assert.Equal(1, count));
        Assert.All(result.Remainders.Take(3), stack =>
        {
            Assert.True(stack!.Is(ItemsRegistry.Bucket));
            Assert.Equal(1, stack.Count);
        });
        Assert.All(result.Remainders.Skip(3), Assert.Null);
        Assert.Equal(2, grid[3]!.Count);
        Assert.Equal(server.CraftingRecipeRevision, result.RecipeRevision);

        // Both the evaluation and identifier lookup are detached, including a lookup's result stacks.
        result.Output.Count = 17;
        result.Remainders[0]!.Count = 17;
        var recipe = Assert.IsType<ShapedRecipe>(server.GetRecipe(result.RecipeIdentifier));
        recipe.Result.First().Count = 17;
        var again = Assert.IsType<CraftingEvaluation>(server.EvaluateCrafting(grid, 3));
        Assert.Equal(1, again.Output.Count);
        Assert.Equal(1, again.Remainders[0]!.Count);
        grid[4] = null;
        Assert.Null(server.EvaluateCrafting(grid, 3));
        Assert.Null(server.GetRecipe("probe:missing"));

        var plugin = Plugin();
        var custom = Shaped($"probe:evaluation_{Guid.NewGuid():N}");
        try
        {
            context.Server.RegisterRecipes([custom], plugin);
            Assert.NotEqual(result.RecipeRevision, server.CraftingRecipeRevision);
            Assert.NotNull(server.GetRecipe(custom.Identifier));
            var registeredRevision = server.CraftingRecipeRevision;
            plugin.UndoHostAdditions();
            Assert.NotEqual(registeredRevision, server.CraftingRecipeRevision);
            Assert.Null(server.GetRecipe(custom.Identifier));
        }
        finally
        {
            plugin.UndoHostAdditions();
        }
    }

    [Fact]
    public void UpdateRecipesUsesVanillaPropertyListsAndStonecutterDisplays()
    {
        var recipes = RecipesRegistry.Current;
        using var writer = new NetworkBuffer();
        recipes.UpdatePacket.Serialize(writer);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        Assert.Equal(9, reader.ReadVarInt());
        var sets = new Dictionary<string, int[]>();
        for (var i = 0; i < 9; i++)
        {
            var name = reader.ReadString();
            sets.Add(name, Enumerable.Range(0, reader.ReadVarInt()).Select(_ => reader.ReadVarInt()).ToArray());
        }

        Assert.Contains(ItemsRegistry.RawIron.Id, sets["minecraft:furnace_input"]);
        Assert.Contains(ItemsRegistry.RawIron.Id, sets["minecraft:blast_furnace_input"]);
        Assert.Contains(ItemsRegistry.Beef.Id, sets["minecraft:smoker_input"]);
        Assert.Contains(ItemsRegistry.Beef.Id, sets["minecraft:campfire_input"]);
        Assert.Contains(ItemsRegistry.DiamondSword.Id, sets["minecraft:smithing_base"]);
        Assert.Contains(ItemsRegistry.NetheriteUpgradeSmithingTemplate.Id, sets["minecraft:smithing_template"]);
        Assert.Contains(ItemsRegistry.NetheriteIngot.Id, sets["minecraft:smithing_addition"]);
        var cutting = recipes.Recipes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value).OfType<CuttingRecipe>().ToArray();
        Assert.Equal(cutting.Length, reader.ReadVarInt());
        foreach (var recipe in cutting)
        {
            var ids = recipe.Ingredient.Select(item => item.Holder.Id).Distinct().ToArray();
            Assert.Equal(ids.Length + 1, reader.ReadVarInt());
            foreach (var id in ids)
                Assert.Equal(id, reader.ReadVarInt());

            Assert.Equal(SlotDisplayTypes.ItemStack, reader.ReadVarInt());
            var result = reader.ReadItemStackTemplate();
            Assert.Equal(recipe.Result.First().Holder, result.Holder);
            Assert.Equal(recipe.Result.First().Count, result.Count);
        }

        Assert.Equal(writer.Size, reader.Offset);
    }


    [Fact(DisplayName = "A plugin's brewing recipe file brews and fills the brewing property sets until it unloads",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BrewingRecipeFileBrewsUntilItsPluginUnloads()
    {
        await using var context = TestServerFactory.CreateServer($"brewing-file-{Guid.NewGuid():N}");
        var plugin = Plugin();
        var barrier = new ItemStack(ItemsRegistry.Barrier, 1);
        var bedrock = new ItemStack(ItemsRegistry.Bedrock, 1);

        // Bedrock brews a barrier into a potion of luck; no vanilla recipe takes either.
        context.Server.LoadRecipeFiles([(plugin, [BrewingFile("probe:luck")])]);

        var loaded = context.Server.RecipeRegistry.Current;
        var brewed = Brewing.Mix(barrier, bedrock, loaded);
        var potion = brewed?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        var loadedSets = PropertySets(loaded);

        Assert.True(brewed?.Is(ItemsRegistry.Potion));
        Assert.Equal(API.Potion.Luck, potion?.Potion);
        Assert.Contains(ItemsRegistry.Barrier.Id, loadedSets["minecraft:brewing_input"]);
        Assert.Contains(ItemsRegistry.Bedrock.Id, loadedSets["minecraft:brewing_reagent"]);

        // Unloading the plugin reloads the recipe files without its own.
        context.Server.LoadRecipeFiles([]);

        var unloaded = context.Server.RecipeRegistry.Current;
        var unloadedSets = PropertySets(unloaded);

        Assert.Null(Brewing.Mix(barrier, bedrock, unloaded));
        Assert.DoesNotContain(ItemsRegistry.Barrier.Id, unloadedSets["minecraft:brewing_input"]);
        Assert.DoesNotContain(ItemsRegistry.Bedrock.Id, unloadedSets["minecraft:brewing_reagent"]);
    }

    [Fact(DisplayName = "A plugin's recipe crafts in the player's grid and leaves it and their book on unload",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task RecipeCraftsUntilItsPluginUnloads()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-recipes-{Guid.NewGuid():N}");
        var server = context.Server;
        var player = await JoinAsync(context, "Crafter");
        var client = (TestClient)player.Client;
        var plugin = Plugin();
        Assert.Single(client.SentPackets.OfType<UpdateRecipesPacket>());

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

            Assert.Equal(2, client.SentPackets.OfType<UpdateRecipesPacket>().Count());

            // The open grid shows the result now, and the player's book has the recipe.
            Assert.True(player.Inventory.GetItem(0)?.Is(ItemsRegistry.Diamond));
            var added = Assert.Single(client.SentPackets.OfType<RecipeBookAddPacket>(), packet => !packet.Replace);
            var entry = Assert.Single(added.Entries);
            Assert.Equal(recipe.Identifier, entry.Recipe.Identifier);
            Assert.NotSame(recipe, entry.Recipe);

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

            Assert.Equal(3, client.SentPackets.OfType<UpdateRecipesPacket>().Count());

            // The result is gone, the client was told, and the recipe no longer matches.
            Assert.Null(player.Inventory.GetItem(0));
            Assert.Null(client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]);
            var removed = Assert.Single(client.SentPackets.OfType<RecipeBookRemovePacket>());
            Assert.Equal(new[] { entry.DisplayId }, removed.DisplayIds);
            Assert.Null(server.RecipeRegistry.Current.FindRecipe(player.Inventory, 2));
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
            var entry = Assert.Single(book.Entries, candidate => candidate.Recipe.Identifier == recipe.Identifier);

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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ServersOwnTheirRecipeBooksAndCrafting()
    {
        await using var first = TestServerFactory.CreateServer($"recipes-first-{Guid.NewGuid():N}");
        await using var second = TestServerFactory.CreateServer($"recipes-second-{Guid.NewGuid():N}");
        var identifier = $"probe:isolated_{Guid.NewGuid():N}";
        var plugin = Plugin();
        first.Server.RegisterRecipes([Shaped(identifier)], plugin);
        var a = await JoinAsync(first, "First");
        var b = await JoinAsync(second, "Second");

        foreach (var player in new[] { a, b })
        {
            foreach (var slot in new[] { 1, 2, 3, 4 })
                player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Bedrock));

            await MainEventHandler.RefreshCraftingResultAsync(player);
        }

        Assert.True(a.Inventory.GetItem(0)?.Is(ItemsRegistry.Diamond));
        Assert.Null(b.Inventory.GetItem(0));
        Assert.DoesNotContain(((TestClient)b.Client).SentPackets.OfType<RecipeBookAddPacket>()
            .SelectMany(packet => packet.Entries), entry => entry.Recipe.Identifier == identifier);

        // The same identifier is independently registrable on B and survives A's unload.
        second.Server.RegisterRecipes(Shaped(identifier));
        await second.Server.SyncRecipesAsync();
        plugin.UndoHostAdditions();
        await first.Server.SyncRecipesAsync();
        Assert.Null(a.Inventory.GetItem(0));
        Assert.True(b.Inventory.GetItem(0)?.Is(ItemsRegistry.Diamond));
        Assert.Empty(((TestClient)b.Client).SentPackets.OfType<RecipeBookRemovePacket>());
        Assert.False(RecipesRegistry.Recipes.ContainsKey(identifier));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DelayedPlacementCannotRestoreAnUnloadedResult()
    {
        await using var context = TestServerFactory.CreateServer($"recipe-placement-race-{Guid.NewGuid():N}");
        var plugin = Plugin();
        var recipe = Shaped($"probe:delayed_{Guid.NewGuid():N}");
        context.Server.RegisterRecipes([recipe], plugin);
        var player = await JoinAsync(context, "Placer");
        var client = (TestClient)player.Client;
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Bedrock, 4));
        var entry = context.Server.RecipeRegistry.Current.Book
            .Single(entry => entry?.Recipe.Identifier == recipe.Identifier)!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = false;
        player.WindowSnapshots = new WindowSnapshots(async packet =>
        {
            if (!delayed && packet is ContainerSetContentPacket)
            {
                delayed = true;
                entered.SetResult();
                await resume.Task;
            }

            return true;
        }, packet => client.SendPacket(packet));

        var placement = MainEventHandler.PlaceBookRecipeAsync(player, 0, entry.DisplayId, false).AsTask();
        await entered.Task;

        try
        {
            plugin.UndoHostAdditions();
            await context.Server.SyncRecipesAsync();
            Assert.Null(player.Inventory.GetItem(0));
        }
        finally
        {
            resume.SetResult();
        }

        await placement;
        Assert.Null(client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]);
        Assert.Equal(4, Enumerable.Range(1, 4).Sum(slot => player.Inventory.GetItem(slot)?.Count ?? 0));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FailedPlayerDoesNotSkipOthersAndCatchesUpOnNextChange()
    {
        await using var context = TestServerFactory.CreateServer($"recipe-failure-{Guid.NewGuid():N}");
        await JoinAsync(context, "First");
        await JoinAsync(context, "Second");
        var players = context.Server.OnlinePlayers.Values.Cast<Entities.Player>().ToArray();
        var failing = players[0];
        var healthy = players[1];
        var client = (TestClient)failing.Client;
        client.ClearPackets();
        var fail = true;
        failing.WindowSnapshots = new WindowSnapshots(packet =>
        {
            if (fail)
                throw new InvalidOperationException("Test packet handler failure.");

            return ValueTask.FromResult(true);
        }, packet => client.SendPacket(packet));

        foreach (var player in players)
        {
            foreach (var slot in new[] { 1, 2, 3, 4 })
                player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Bedrock));
        }

        context.Server.RegisterRecipes(Shaped($"probe:failure_{Guid.NewGuid():N}"));
        await context.Server.SyncRecipesAsync();
        Assert.True(healthy.Inventory.GetItem(0)?.Is(ItemsRegistry.Diamond));
        Assert.True(((TestClient)healthy.Client).SentPackets.OfType<ContainerSetContentPacket>()
            .Last().Items[0]?.Is(ItemsRegistry.Diamond));
        Assert.Empty(client.SentPackets.OfType<ContainerSetContentPacket>());
        fail = false;

        context.Server.RegisterRecipes(Shaped($"probe:retry_{Guid.NewGuid():N}"));
        await context.Server.SyncRecipesAsync();
        Assert.True(client.SentPackets.OfType<ContainerSetContentPacket>().Last().Items[0]?.Is(ItemsRegistry.Diamond));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task BookSyncDefersUnconfiguredContent()
    {
        await using var context = TestServerFactory.CreateServer($"recipe-content-{Guid.NewGuid():N}");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await peer.ConnectAsync(listener.LocalEndpoint);
        using var socket = await listener.AcceptSocketAsync();
        using var client = new Client(
            context.EventDispatcher, context.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), null!, context.Provider, null!);

        // Exercise the real queue handlers without starting the socket send/receive loops.
        typeof(Client).GetProperty(nameof(Client.Socket))!.SetValue(client, socket);
        client.SetState(ClientState.Play);
        var player = new Entities.Player(Guid.NewGuid(), "Configuring", client, context.World)
        {
            Server = context.Server
        };
        var first = Shaped($"probe:configured_{Guid.NewGuid():N}");
        context.Server.RegisterRecipes(first);
        var packets = new List<RecipeBookAddPacket>();
        context.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(args =>
        {
            if (args.Packet is RecipeBookAddPacket book)
                packets.Add(book);

            args.Cancel();
            return ValueTask.CompletedTask;
        }), Priority.Critical);

        // The handlers see the packets in the client's pipeline, once their turn comes.
        var current = context.Server.GetContentVersion();
        var older = current with { Registries = "before-the-new-items" };
        client.ConfiguredContent = older;
        await player.RecipeBookSync.SendInitialAsync(player);
        await client.FlushAsync();
        Assert.DoesNotContain(packets.Last().Entries, entry => entry.Recipe.Identifier == first.Identifier);

        client.ConfiguredContent = current;
        await player.RecipeBookSync.SendInitialAsync(player);
        await client.FlushAsync();
        Assert.Contains(packets.Last().Entries, entry => entry.Recipe.Identifier == first.Identifier);

        // A change the client's configured content doesn't cover yet waits for it.
        var second = Shaped($"probe:while_waiting_{Guid.NewGuid():N}");
        context.Server.RegisterRecipes(second);
        client.ConfiguredContent = older;
        Assert.False(await player.RecipeBookSync.SendChangesAsync(player));
        await client.FlushAsync();
        Assert.DoesNotContain(packets.SelectMany(packet => packet.Entries),
            entry => entry.Recipe.Identifier == second.Identifier);

        client.ConfiguredContent = context.Server.GetContentVersion();
        Assert.True(await player.RecipeBookSync.SendChangesAsync(player));
        await client.FlushAsync();
        Assert.False(packets.Last().Replace);
        Assert.Equal(second.Identifier, Assert.Single(packets.Last().Entries).Recipe.Identifier);
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

    // A brewing recipe file in which bedrock brews a barrier into a potion of luck.
    private static RecipeFile BrewingFile(string identifier)
    {
        using var json = JsonDocument.Parse("""
            {
                "type": "minecraft:brewing",
                "input": { "item": "minecraft:barrier" },
                "reagent": { "item": "minecraft:bedrock" },
                "output": {
                    "id": "minecraft:potion",
                    "components": { "minecraft:potion_contents": { "potion": "minecraft:luck" } }
                }
            }
            """);

        return new RecipeFile(identifier, json.RootElement.Clone());
    }

    // The property sets an update recipes packet sends, by name.
    private static Dictionary<string, int[]> PropertySets(RecipeSet recipes)
    {
        using var writer = new NetworkBuffer();
        recipes.UpdatePacket.Serialize(writer);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());

        var sets = new Dictionary<string, int[]>();
        var count = reader.ReadVarInt();
        for (var set = 0; set < count; set++)
        {
            var name = reader.ReadString();
            var ids = Enumerable.Range(0, reader.ReadVarInt()).Select(_ => reader.ReadVarInt()).ToArray();
            sets.Add(name, ids);
        }

        return sets;
    }

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
