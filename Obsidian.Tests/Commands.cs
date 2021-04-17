using Obsidian.API;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests
{
    public class Commands
    {
        [Fact]
        public async Task TestTokenizing()
        {
            await Task.Yield();
            var message = "/test help help \"help help \\n\" help";
            var expected = new[] { "test", "help", "help", "help help \n", "help" };

            var cmd = new CommandParser("/");

            cmd.IsCommandQualified(message, out string qualified);
            var split = cmd.SplitQualifiedString(qualified);
            Assert.Equal(split, expected);
        }

        // TODO overload support is not here yet, there has to be a loop through qualified commands in CommandHandler.cs:executeCommand
        [Fact]
        public async Task TestCommandExec()
        {
            var cmd = new CommandHandler("/");

            cmd.RegisterCommandClass<Command>(null, new Command());

            await cmd.ProcessCommand(new CommandContext("/ping 69 hello", null, null, null));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("hello", Command.arg2out);

            await cmd.ProcessCommand(new CommandContext("/pong ping 420 bye", null, null, null));
            Assert.Equal(420, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);

            await cmd.ProcessCommand(new CommandContext("/ping 12 12", null, null, null));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);

            await cmd.ProcessCommand(new CommandContext("/ping 69 hey bye", null, null, null));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);
        }

        public class Command
        {
            public static int arg1out = 0;
            public static string arg2out = "";

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(CommandContext ctx, int arg1, int arg2)
            {
                await Task.Yield();
            }

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(CommandContext ctx, int arg1, string arg2)
            {
                await Task.Yield();
                arg1out = arg1;
                arg2out = arg2;
            }

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(CommandContext ctx, int arg1, string arg2, string arg3)
            {
                await Task.Yield();
            }

            [CommandGroup("pong")]
            [CommandInfo(description: "pong")]
            public class Pong
            {
                [Command("ping")]
                [CommandInfo(description: "ping")]
                public async Task ping(CommandContext ctx, int arg1, string arg2)
                {
                    await Task.Yield();
                    arg1out = arg1;
                    arg2out = arg2;
                }
            }
        }
    }
}
