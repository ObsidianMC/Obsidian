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

            await cmd.ProcessCommand(new BaseCommandContext("/ping"));
            await cmd.ProcessCommand(new BaseCommandContext("/pong ping"));
        }

        public class Command : BaseCommandClass
        {
            [Command("ping")]
            public async Task ping()
            {

            }

            [CommandGroup("pong")]
            public class Pong
            {
                [Command("ping")]
                public async Task ping()
                {

                }
            }
        }
    }
}
