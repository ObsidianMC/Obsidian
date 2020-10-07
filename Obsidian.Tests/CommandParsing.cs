using Xunit;
using Obsidian.CommandFramework;

namespace Obsidian.Tests
{
    public class CommandParsing
    {
        private readonly CommandHandler commandHandler = new CommandHandler();
        
        [Fact]
        public void Parsing()
        {
            TryParse(@"/command arg1  ""arg2 with \"" character"" arg3 ",
                "/command", "arg1", @"arg2 with "" character", "arg3");
        }

        private void TryParse(string text, params string[] expected)
        {
            string[] result = commandHandler.ParseCommand(text).ToArray();

            Assert.Equal(result, expected);
        }
    }
}
