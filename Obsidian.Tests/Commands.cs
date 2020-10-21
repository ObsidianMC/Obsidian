using Obsidian.CommandFramework;
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

    }
}
