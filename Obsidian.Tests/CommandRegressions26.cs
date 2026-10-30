using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Regression tests for the 26.x commands where Obsidian's own plumbing could lose items or throw.</summary>
public sealed class CommandRegressions26
{
    // The player inventory index of hotbar.0.
    private const int FirstHotbarSlot = 36;

    [Theory]
    [InlineData("replace")]
    [InlineData("fill")]
    [InlineData("override")]
    public async Task EmptyItemSourcesLeaveTargetsAlone(string operation)
    {
        await using var server = TestServerFactory.CreateServer($"item-empty-source-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ItemCommandModule>(null);
        var player = AddPlayer(server);
        player.Inventory.SetItem(FirstHotbarSlot, new ItemStack(ItemsRegistry.Stone, 5));
        var command = $"item {operation} entity Fixture hotbar.0 from entity Fixture {{type:'empty'}}";
        var context = Context(server.Server, command);

        await handler.ProcessCommand(context);

        Assert.False(context.Succeeded);
        Assert.Equal(0, context.Result);
        var stack = player.Inventory.GetItem(FirstHotbarSlot)!;
        Assert.True(stack.Is(ItemsRegistry.Stone));
        Assert.Equal(5, stack.Count);
    }

    [Fact]
    public async Task ItemModifySkipsEmptySlots()
    {
        await using var server = TestServerFactory.CreateServer($"item-modify-empty-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ItemCommandModule>(null);
        var player = AddPlayer(server);
        var empty = Context(server.Server, "item modify entity Fixture hotbar.* {type:'set_count',count:3}");

        await handler.ProcessCommand(empty);
        player.Inventory.SetItem(FirstHotbarSlot + 4, new ItemStack(ItemsRegistry.Stone, 5));
        var one = Context(server.Server, "item modify entity Fixture hotbar.* {type:'set_count',count:3}");
        await handler.ProcessCommand(one);

        Assert.False(empty.Succeeded);
        Assert.Equal(0, empty.Result);
        Assert.True(one.Succeeded);
        Assert.Equal(1, one.Result);
        for (var slot = 0; slot < 9; slot++)
        {
            var stack = player.Inventory.GetItem(FirstHotbarSlot + slot);
            var count = stack is { IsAir: false } ? stack.Count : 0;
            Assert.Equal(slot == 4 ? 3 : 0, count);
        }
    }

    [Theory]
    [InlineData("posteffect list MissingPlayer", false)]
    [InlineData("posteffect list @a", false)]
    [InlineData("posteffect list @e[type=zombie]", false)]
    [InlineData("posteffect clear MissingPlayer", false)]
    [InlineData("posteffect list Fixture", true)]
    public async Task PostEffectSelectorFailuresFailTheCommand(string command, bool succeeds)
    {
        await using var server = TestServerFactory.CreateServer($"posteffect-selector-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<PostEffectCommandModule>(null);
        AddPlayer(server);
        var context = Context(server.Server, command);

        await handler.ProcessCommand(context);

        Assert.Equal(succeeds, context.Succeeded);
        Assert.Equal(0, context.Result);
    }

    private static Obsidian.Entities.Player AddPlayer(RealServerContext server)
    {
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Fixture");
        server.Server.AddPlayer(player);
        return player;
    }

    private static CommandContext Context(Server server, string command) =>
        new("/" + command, new CommandSender(CommandIssuers.Console, null), null, server);
}
