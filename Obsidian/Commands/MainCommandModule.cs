using Qmmands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class MainCommandModule : ModuleBase<CommandContext>
    {
        public CommandService Service { get; set; }

        [Command("help", "commands")]
        [Description("Lists available commands.")]
        public Task HelpAsync()
            => Context.Client.SendChatAsync(string.Join('\n', Service.GetAllCommands().Select(x => $"{MinecraftColor.DarkGreen}{x.Name}{MinecraftColor.Reset}: {x.Description}")));

        [Command("echo")]
        [Description("Echoes given text.")]
        public Task EchoAsync([Remainder] string text)
            => Context.Server.SendChatAsync(text, Context.Client, system: true);

        [Command("announce")]
        [Description("makes an announcement")]
        public Task AnnounceAsync([Remainder] string text)
            => Context.Server.SendChatAsync(text, Context.Client, 2, true);
    }
}
