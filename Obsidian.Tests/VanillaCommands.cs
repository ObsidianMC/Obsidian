using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Commands.ArgumentParsers;
using Obsidian.API.Registries;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Nbt;
using Obsidian.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public class VanillaCommands(ITestOutputHelper output)
{
    [Fact(DisplayName = "Every built-in command's parameters have argument parsers, and command names are unique")]
    public void BuiltInCommandsHaveParsers()
    {
        var handler = this.CreateHandler();
        handler.RegisterCommands();

        var commands = handler.GetAllCommands();
        foreach (var command in commands)
        {
            foreach (var parameter in command.Overloads.SelectMany(overload => overload.GetParameters()))
                Assert.True(handler.IsValidArgumentType(parameter.ParameterType), $"/{command.Name} {parameter.Name}: {parameter.ParameterType}");
        }

        var duplicates = commands.Where(x => x.Parent is null).GroupBy(x => x.Name).Where(x => x.Count() > 1).Select(x => x.Key);
        Assert.Empty(duplicates);

        string[] vanilla = ["kill", "effect", "enchant", "summon", "setblock", "fill", "clone", "execute", "scoreboard", "team",
            "gamerule", "difficulty", "weather", "experience", "kick", "ban", "ban-ip", "pardon", "pardon-ip", "banlist", "whitelist",
            "particle", "playsound", "stopsound", "data", "say", "me", "msg", "list", "seed", "clear", "defaultgamemode",
            "setworldspawn", "tellraw", "title", "give", "gamemode", "tp"];
        Assert.All(vanilla, name => Assert.Contains(commands, x => x.Parent is null && x.Name == name));
    }

    [Fact(DisplayName = "Positions take three words wherever they are in a command")]
    public async Task BindsMultiWordArguments()
    {
        var handler = this.CreateHandler();
        handler.RegisterCommandClass<PositionModule>(null);
        var sender = new CommandSender(CommandIssuers.Console, player: null);

        await handler.ProcessCommand(new CommandContext("/place 1 -2 3 stone", sender, null, null));
        Assert.Equal((new Vector(1, -2, 3), "stone"), PositionModule.Placed);

        await handler.ProcessCommand(new CommandContext("/place ~1 ~ ~-1 dirt", sender, null, null) { Origin = new VectorD(10.5, 64, -3.2) });
        Assert.Equal((new Vector(11, 64, -5), "dirt"), PositionModule.Placed);

        await handler.ProcessCommand(new CommandContext("/move 1 2 3", sender, null, null));
        Assert.Equal(new VectorD(1.5, 2, 3.5), PositionModule.Moved);
    }

    [Fact(DisplayName = "Suggestions find the parameter after a position")]
    public async Task SuggestsAfterMultiWordArguments()
    {
        var handler = this.CreateHandler();
        handler.RegisterCommandClass<PositionModule>(null);
        var sender = new CommandSender(CommandIssuers.Client, player: null);

        var completion = await handler.CompleteAsync(new CommandContext("/place 1 2 3 st", sender, null, null));

        Assert.Equal(["stone"], completion.Suggestions.Select(x => x.Text));
    }

    [Theory(DisplayName = "Target selectors parse like vanilla's")]
    [InlineData("@a", true)]
    [InlineData("@e[type=zombie,limit=2,sort=nearest]", true)]
    [InlineData("@e[type=!minecraft:pig,distance=..5]", true)]
    [InlineData("@p[distance=2..8,gamemode=!creative]", true)]
    [InlineData("@n", true)]
    [InlineData("Notch", true)]
    [InlineData("069a79f4-44e9-4726-a5be-fca90e38aaf5", true)]
    [InlineData("@x", false)]
    [InlineData("@e[limit=0]", false)]
    [InlineData("@e[unknown=1]", false)]
    [InlineData("@e[type=not_a_mob]", false)]
    [InlineData("@s[limit=2]", false)]
    [InlineData("@e[distance=5..1]", false)]
    [InlineData("bad name!", false)]
    public void ParsesSelectors(string input, bool valid) => Assert.Equal(valid, EntitySelector.TryParse(input, out _));

    [Fact(DisplayName = "Single-entity selectors are told apart")]
    public void TellsSingleSelectors()
    {
        Assert.True(EntitySelector.TryParse("@p", out var nearest) && nearest.Single);
        Assert.True(EntitySelector.TryParse("@e[limit=1]", out var limited) && limited.Single);
        Assert.True(EntitySelector.TryParse("@e", out var all) && !all.Single);
        Assert.True(EntitySelector.TryParse("@a", out var players) && players.PlayersOnly);
    }

    [Theory(DisplayName = "Coordinates are absolute, relative or local")]
    [InlineData("1 64 2", 1.5, 64, 2.5)]
    [InlineData("1.25 64 -2.75", 1.25, 64, -2.75)]
    [InlineData("~ ~1 ~-2", 10, 65, 18)]
    [InlineData("^ ^ ^1", 10, 64, 21)] // facing south, +z
    [InlineData("^1 ^ ^", 11, 64, 20)] // left of south is east, +x
    [InlineData("^ ^2 ^", 10, 66, 20)]
    public void ReadsCoordinates(string input, double x, double y, double z)
    {
        var context = new CommandContext("/", new CommandSender(CommandIssuers.Console, null), null, null)
        {
            Origin = new VectorD(10, 64, 20),
            Rotation = (0f, 0f)
        };

        Assert.True(CommandCoordinates.TryParse(input, context, centerBlocks: true, wholeNumbers: false, out var position));
        Assert.Equal(x, position.X, 6);
        Assert.Equal(y, position.Y, 6);
        Assert.Equal(z, position.Z, 6);
    }

    [Theory(DisplayName = "Malformed coordinates are refused")]
    [InlineData("1 2")]
    [InlineData("^ ~ ^")]
    [InlineData("1 two 3")]
    [InlineData("~ ~ ~ ~")]
    public void RefusesCoordinates(string input)
    {
        var context = new CommandContext("/", new CommandSender(CommandIssuers.Console, null), null, null) { Origin = default, Rotation = (0f, 0f) };

        Assert.False(CommandCoordinates.TryParse(input, context, centerBlocks: true, wholeNumbers: false, out _));
    }

    [Fact(DisplayName = "Block positions need whole numbers")]
    public void ReadsBlockPositions()
    {
        var parser = new BlockPositionArgumentParser();
        var context = new CommandContext("/", new CommandSender(CommandIssuers.Console, null), null, null) { Origin = new VectorD(0.7, 64.2, -0.3) };

        Assert.True(parser.TryParseArgument("~ ~ ~", context, out var here));
        Assert.Equal(new Vector(0, 64, -1), here);
        Assert.False(parser.TryParseArgument("1.5 2 3", context, out _));
    }

    [Fact(DisplayName = "Registry arguments read vanilla identifiers")]
    public void ReadsRegistryIdentifiers()
    {
        var context = new CommandContext("/", new CommandSender(CommandIssuers.Console, null), null, null);

        Assert.True(new PotionEffectArgumentParser().TryParseArgument("minecraft:jump_boost", context, out var effect));
        Assert.Equal(PotionEffect.JumpBoost, effect);

        Assert.True(new EntityTypeArgumentParser().TryParseArgument("zombified_piglin", context, out var type));
        Assert.Equal(EntityType.ZombifiedPiglin, type);

        Assert.True(new ItemArgumentParser().TryParseArgument("diamond_sword[damage=3]", context, out var item));
        Assert.Equal(ItemsRegistry.Get("minecraft:diamond_sword").Id, item.Id);
        Assert.False(new ItemArgumentParser().TryParseArgument("minecraft:not_an_item", context, out _));

        Assert.True(new BlockStateArgumentParser().TryParseArgument("oak_stairs[facing=east,half=top]{}", context, out var block));
        Assert.Equal("minecraft:oak_stairs", block.Name);
        Assert.Equal("east", block.Properties["facing"]);

        Assert.True(new EnchantmentArgumentParser().TryParseArgument("sharpness", context, out var enchantment));
        Assert.Equal("minecraft:sharpness", enchantment.Identifier);

        Assert.False(ResourceLocation.TryParse("Upper:case", out _));
    }

    [Fact(DisplayName = "Stringified NBT looks like vanilla's")]
    public void WritesSnbt()
    {
        var tag = new NbtCompound
        {
            new NbtTag<float>("Health", 20),
            new NbtTag<string>("id", "minecraft:pig"),
            new NbtList(NbtTagType.Double, "Pos") { new NbtTag<double>("", 0.5), new NbtTag<double>("", 64) },
            new NbtTag<long>("Seed", 7)
        };

        Assert.Equal("{Health: 20.0f, Pos: [0.5d, 64.0d], Seed: 7L, id: \"minecraft:pig\"}", Snbt.Write(tag));
        Assert.Equal("64.0d", Snbt.Write(DataCommandModule.Find(tag, "Pos[-1]")!));
    }

    [Fact(DisplayName = "Bans match by name or UUID and can be lifted")]
    public void BansPlayers()
    {
        var bans = new BanList(NullLogger<BanList>.Instance, persistent: false);
        var uuid = Guid.NewGuid();

        Assert.True(bans.Ban("Steve", uuid, "Server", null));
        Assert.False(bans.Ban("steve", null, "Server", null));
        Assert.NotNull(bans.GetBan(uuid, "Renamed"));
        Assert.NotNull(bans.GetBan(Guid.Empty, "STEVE"));

        Assert.True(bans.Pardon("Steve"));
        Assert.Null(bans.GetBan(uuid, "Steve"));

        Assert.True(bans.BanIp("10.0.0.1", "Server", "spam"));
        Assert.Equal("spam", bans.GetBan("10.0.0.1")!.Reason);
        Assert.True(bans.PardonIp("10.0.0.1"));
    }

    [Fact(DisplayName = "Scoreboard display slots read vanilla's names")]
    public void ReadsDisplaySlots()
    {
        Assert.Equal(DisplaySlot.Sidebar, ScoreboardCommandModule.ParseSlot("sidebar"));
        Assert.Equal(DisplaySlot.BelowName, ScoreboardCommandModule.ParseSlot("below_name"));
        Assert.Equal(DisplaySlot.TeamDarkAqua, ScoreboardCommandModule.ParseSlot("sidebar.team.dark_aqua"));
        Assert.Equal(DisplaySlot.TeamWhite, ScoreboardCommandModule.ParseSlot("sidebar.team.white"));
        Assert.Null(ScoreboardCommandModule.ParseSlot("sidebar.team.reset"));
    }

    [Fact(DisplayName = "Text components are read from JSON or taken as written")]
    public void ReadsTextComponents()
    {
        Assert.Equal("Hi", TextCommandModule.ParseComponent("{\"text\":\"Hi\"}").Text);
        Assert.Equal("Hi", TextCommandModule.ParseComponent("\"Hi\"").Text);
        Assert.Equal("{broken", TextCommandModule.ParseComponent("{broken").Text);
    }

    private CommandHandler CreateHandler()
    {
        var services = new ServiceCollection()
            .AddLogging(builder => builder.AddXUnit(output))
            .AddSingleton<CommandHandler>()
            .BuildServiceProvider();

        return services.GetRequiredService<CommandHandler>();
    }

    public sealed class PositionModule : CommandModuleBase
    {
        public static (Vector, string) Placed;
        public static VectorD Moved;

        [Command("place")]
        [IssuerScope(CommandIssuers.Any)]
        public Task Place(Vector pos, [Suggestions("stone", "dirt")] string block)
        {
            Placed = (pos, block);
            return Task.CompletedTask;
        }

        [Command("move")]
        [IssuerScope(CommandIssuers.Any)]
        public Task Move(VectorD to)
        {
            Moved = to;
            return Task.CompletedTask;
        }
    }
}
