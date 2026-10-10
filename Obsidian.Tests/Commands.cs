using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public class Commands
{
    private readonly ITestOutputHelper output;

    public Commands(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void TestTokenizing()
    {
        var message = "/test help help \"help help \\n\" help";
        var expected = new[] { "test", "help", "help", "help help \n", "help" };

        var cmd = new Obsidian.Commands.Framework.CommandParser("/");

        cmd.IsCommandQualified(message, out ReadOnlyMemory<char> qualified);
        var split = Obsidian.Commands.Framework.CommandParser.SplitQualifiedString(qualified);
        Assert.Equal(split, expected);
    }

    [Fact]
    public async Task TestCommandExec()
    {
        var services = new ServiceCollection()
            .AddLogging((builder) => builder.AddXUnit(this.output))
            .AddSingleton<CommandHandler>()
            .BuildServiceProvider();

        var cmd = services.GetRequiredService<CommandHandler>();

        ICommandSender sender = new CommandSender(CommandIssuers.Console, player: null);

        cmd.RegisterCommandClass<Command>(null);

        await cmd.ProcessCommand(new CommandContext("/ping 69 hello", sender, null, null));
        Assert.Equal(69, Command.arg1out);
        Assert.Equal("hello", Command.arg2out);

        await cmd.ProcessCommand(new CommandContext("/pong ping 420 bye", sender, null, null));
        Assert.Equal(420, Command.arg1out);
        Assert.Equal("bye", Command.arg2out);

        await cmd.ProcessCommand(new CommandContext("/ping 12 12", sender, null, null));
        Assert.Equal(12, Command.arg1out);
        Assert.Equal("bye", Command.arg2out);

        await cmd.ProcessCommand(new CommandContext("/ping 69 hey bye", sender, null, null));
        Assert.Equal(69, Command.arg1out);
        Assert.Equal("bye", Command.arg2out);
    }

    [Theory(DisplayName = "Command lines complete to subcommands and suggested argument values")]
    [InlineData("/sug", 1, 3, "suggest")]
    [InlineData("/suggest ", 9, 0, "g,give,mix,paint,set")] // not the denied locked group
    [InlineData("/suggest set n", 13, 1, "night,noon")]
    [InlineData("/suggest set ", 13, 0, "day,midnight,night,noon")]
    [InlineData("suggest set N", 12, 1, "night,noon")] // command blocks omit the prefix
    [InlineData("/suggest set day", 13, 3, "")]
    [InlineData("/suggest give 5 d", 16, 1, "dirt")]
    [InlineData("/suggest g 5 d", 13, 1, "dirt")]
    [InlineData("/suggest give x d", 16, 1, "")] // x doesn't parse as the amount
    [InlineData("/suggest paint g", 15, 1, "green")] // from the argument parser
    [InlineData("/suggest mix r", 13, 1, "red bike")]
    [InlineData("/suggest mix red b", 13, 5, "red bike,red blue")] // the second word's suggestion is widened
    [InlineData("/suggest locked open n", 21, 1, "")]
    [InlineData("/missing n", 9, 1, "")]
    public async Task CompletesCommandLines(string input, int start, int length, string expected)
    {
        var handler = this.CreateSuggestionHandler();

        var sender = new CommandSender(CommandIssuers.Client, player: null);
        var completion = await handler.CompleteAsync(new CommandContext(input, sender, null, null));

        Assert.Equal((start, length), (completion.Start, completion.Length));
        Assert.Equal(expected, string.Join(',', completion.Suggestions.Select(x => x.Text)));
    }

    [Fact(DisplayName = "Group checks cover subcommands, and overloads marked as commands register once")]
    public async Task AppliesGroupChecksAndRegistersOverloadsOnce()
    {
        var handler = this.CreateSuggestionHandler();

        var sender = new CommandSender(CommandIssuers.Console, player: null);
        await handler.ProcessCommand(new CommandContext("/suggest locked open now", sender, null, null));

        Assert.False(SuggestionModule.Locked.Ran);
        Assert.Single(handler.GetAllCommands(), x => x.Name == "set");
    }

    private CommandHandler CreateSuggestionHandler()
    {
        var services = new ServiceCollection()
            .AddLogging((builder) => builder.AddXUnit(this.output))
            .AddSingleton<CommandHandler>()
            .BuildServiceProvider();

        var handler = services.GetRequiredService<CommandHandler>();
        handler.TryAddArgumentParser(new ColorParser());
        handler.RegisterCommandClass<SuggestionModule>(null);

        return handler;
    }

    public enum Color { Red, Green }

    private sealed class ColorParser : BaseArgumentParser<Color>, ISuggestionProvider
    {
        public override int Id => 5;
        public override string Identifier => "brigadier:string";

        public override bool TryParseArgument(string input, CommandContext ctx, out Color result) =>
            Enum.TryParse(input, ignoreCase: true, out result);

        public ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult<IEnumerable<CommandSuggestion>>([new("red"), new("green")]);
    }

    private sealed class DenyAttribute : BaseExecutionCheckAttribute
    {
        public override Task<bool> RunChecksAsync(CommandContext context) => Task.FromResult(false);
    }

    [CommandGroup("suggest")]
    public class SuggestionModule : CommandModuleBase
    {
        [Command("set")]
        public Task Set([Suggestions("noon", "day", "night", "midnight")] string value) => Task.CompletedTask;

        // Repeats the CommandAttribute, which must not register a second "set".
        [Command("set")]
        [CommandOverload]
        public Task Set(int value) => Task.CompletedTask;

        [Command("give", "g")]
        public Task Give(int amount, [Suggestions("stone", "dirt")] string item) => Task.CompletedTask;

        [Command("mix")]
        public Task Mix([Remaining, Suggestions("red bike")] string text) => Task.CompletedTask;

        [CommandOverload]
        public Task Mix(string first, [Suggestions("blue")] string second) => Task.CompletedTask;

        [Command("paint")]
        public Task Paint(Color color) => Task.CompletedTask;

        [CommandGroup("locked")]
        [Deny]
        public class Locked : CommandModuleBase
        {
            public static bool Ran;

            [Command("open")]
            public Task Open([Suggestions("now")] string when)
            {
                Ran = true;
                return Task.CompletedTask;
            }
        }
    }

    public class Command : CommandModuleBase
    {
        public static int arg1out;
        public static string arg2out = "";

        [Command("ping")]
        [CommandInfo(description: "ping")]
        [IssuerScope(CommandIssuers.Any)]
        public async Task ping(int arg1, int arg2)
        {
            await Task.Yield();
            arg1out = arg1;
        }

        [CommandOverload]
        public async Task ping(int arg1, string arg2)
        {
            await Task.Yield();
            arg1out = arg1;
            arg2out = arg2;
        }

        [CommandOverload]
        public async Task ping(int arg1, string arg2, string arg3)
        {
            await Task.Yield();
            arg1out = arg1;
        }

        [CommandGroup("pong")]
        [CommandInfo(description: "pong")]
        [IssuerScope(CommandIssuers.Any)]
        public class Pong : CommandModuleBase
        {
            [Command("ping")]
            [CommandInfo(description: "ping")]
            [IssuerScope(CommandIssuers.Any)]
            public async Task ping(int arg1, string arg2)
            {
                await Task.Yield();
                arg1out = arg1;
                arg2out = arg2;
            }
        }
    }
}
