using Obsidian.CommandFramework;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests
{
    public class Commands
    {
        [Fact]
        public async Task TestTokenizing()
        {
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

            cmd.RegisterCommandClass<Command>();

            await cmd.ProcessCommand(new ObsidianContext("/ping 69 hello", null, null));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("hello", Command.arg2out);

            await cmd.ProcessCommand(new ObsidianContext("/pong ping 420 bye", null, null));
            Assert.Equal(420, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);

            await cmd.ProcessCommand(new ObsidianContext("/ping 12 12", null, null));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);

            await cmd.ProcessCommand(new ObsidianContext("/ping 69 hey bye", null, null));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);
        }

        public class Command : BaseCommandClass
        {
            public static int arg1out = 0;
            public static string arg2out = "";

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(ObsidianContext ctx, int arg1, int arg2)
            {
            }

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(ObsidianContext ctx, int arg1, string arg2)
            {
                arg1out = arg1;
                arg2out = arg2;
            }

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(ObsidianContext ctx, int arg1, string arg2, string arg3)
            {
            }

            [CommandGroup("pong")]
            [CommandInfo(description: "pong")]
            public class Pong
            {
                [Command("ping")]
                [CommandInfo(description: "ping")]
                public async Task ping(ObsidianContext ctx, int arg1, string arg2)
                {
                    arg1out = arg1;
                    arg2out = arg2;
                }
            }
        }
    }
}
