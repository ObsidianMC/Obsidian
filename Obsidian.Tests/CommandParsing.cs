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
            TryParse(text: @"/command arg1  ""arg2 with \"" character"" arg3 ",
                     expected: new[] { "/command", "arg1", @"arg2 with "" character", "arg3" });
        }

        private void TryParse(string text, string[] expected)
        {
            string[] result = commandHandler.ParseCommand(text).ToArray();

            Assert.Equal(expected, result);
        }
    }
}
