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

        [Fact]
        public async Task TestCommandExec()
        {
            var cmd = new CommandHandler("/");

            cmd.RegisterCommandClass<Command>();
            cmd.RegisterContextType<BaseCommandContext>();

            await cmd.ProcessCommand(new BaseCommandContext("/ping 69 hello"));
            Assert.Equal(69, Command.arg1out);
            Assert.Equal("hello", Command.arg2out);

            await cmd.ProcessCommand(new BaseCommandContext("/pong ping 420 bye"));
            Assert.Equal(420, Command.arg1out);
            Assert.Equal("bye", Command.arg2out);
        }

        public class Command : BaseCommandClass
        {
            public static int arg1out = 0;
            public static string arg2out = "";

            [Command("ping")]
            [CommandInfo(description: "ping")]
            public async Task ping(int arg1, string arg2)
            {
                arg1out = arg1;
                arg2out = arg2;
            }

            [CommandGroup("pong")]
            [CommandInfo(description: "pong")]
            public class Pong
            {
                [Command("ping")]
                [CommandInfo(description: "ping")]
                public async Task ping(int arg1, string arg2)
                {
                    arg1out = arg1;
                    arg2out = arg2;
                }
            }
        }
    }
}
