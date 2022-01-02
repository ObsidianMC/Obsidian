using Obsidian.API;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Commands
{
    [Fact]
    public void TestTokenizing()
    {
        var message = "/test help help \"help help \\n\" help";
        var expected = new[] { "test", "help", "help", "help help \n", "help" };

        var cmd = new CommandParser("/");

        cmd.IsCommandQualified(message, out ReadOnlyMemory<char> qualified);
        var split = CommandParser.SplitQualifiedString(qualified);
        Assert.Equal(split, expected);
    }

    // TODO overload support is not here yet, there has to be a loop through qualified commands in CommandHandler.cs:executeCommand
    [Fact]
    public async Task TestCommandExec()
    {
        var cmd = new CommandHandler("/");

        ICommandSender sender = new CommandSender(CommandIssuers.Console, player: null, logger: null);

        cmd.RegisterCommandClass<Command>(null, new Command());

        await cmd.ProcessCommand(new CommandContext("/ping 69 hello", sender, null, null));
        Assert.Equal(69, Command.arg1out);
        Assert.Equal("hello", Command.arg2out);

        await cmd.ProcessCommand(new CommandContext("/pong ping 420 bye", sender, null, null));
        Assert.Equal(420, Command.arg1out);
        Assert.Equal("bye", Command.arg2out);

        await cmd.ProcessCommand(new CommandContext("/ping 12 12", sender, null, null));
        Assert.Equal(69, Command.arg1out);
        Assert.Equal("bye", Command.arg2out);

        await cmd.ProcessCommand(new CommandContext("/ping 69 hey bye", sender, null, null));
        Assert.Equal(69, Command.arg1out);
        Assert.Equal("bye", Command.arg2out);
    }

    public class Command
    {
        public static int arg1out = 0;
        public static string arg2out = "";

        [Command("ping")]
        [CommandInfo(description: "ping")]
        [IssuerScope(CommandIssuers.Any)]
        public async Task ping(CommandContext ctx, int arg1, int arg2)
        {
            await Task.Yield();
        }

        [Command("ping")]
        [CommandInfo(description: "ping")]
        [IssuerScope(CommandIssuers.Any)]
        public async Task ping(CommandContext ctx, int arg1, string arg2)
        {
            await Task.Yield();
            arg1out = arg1;
            arg2out = arg2;
        }

        [Command("ping")]
        [CommandInfo(description: "ping")]
        [IssuerScope(CommandIssuers.Any)]
        public async Task ping(CommandContext ctx, int arg1, string arg2, string arg3)
        {
            await Task.Yield();
        }

        [CommandGroup("pong")]
        [CommandInfo(description: "pong")]
        [IssuerScope(CommandIssuers.Any)]
        public class Pong
        {
            [Command("ping")]
            [CommandInfo(description: "ping")]
            [IssuerScope(CommandIssuers.Any)]
            public async Task ping(CommandContext ctx, int arg1, string arg2)
            {
                await Task.Yield();
                arg1out = arg1;
                arg2out = arg2;
            }
        }
    }
}
