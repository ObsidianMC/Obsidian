using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using System;
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

    public class Command : CommandModuleBase
    {
        public static int arg1out = 0;
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
