using Obsidian.Entities;
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
        public async Task HelpAsync()
        {
            foreach(var cmd in Service.GetAllCommands())
            {
                await Context.Client.SendChatAsync($"{MinecraftColor.DarkGreen}{cmd.Name}{MinecraftColor.Reset}: {cmd.Description}");
            }
        }

        [Command("plugins")]
        [Description("Lists plugins.")]
        public Task PluginsAsync()
            => Context.Client.SendChatAsync(string.Join('\n', Context.Server.PluginManager.Plugins.Select(x 
                => $"{MinecraftColor.DarkGreen}{x.Info.Name}by {x.Info.Author}\n{MinecraftColor.Reset} {x.Info.Description}")));

        [Command("echo")]
        [Description("Echoes given text.")]
        public Task EchoAsync([Remainder] string text)
            => Context.Server.SendChatAsync(text, Context.Client, system: true);

        [Command("announce")]
        [Description("makes an announcement")]
        public Task AnnounceAsync([Remainder] string text)
            => Context.Server.SendChatAsync(text, Context.Client, 2, true);

        [Command("leave", "kickme")]
        [Description("kicks you")]
        public Task LeaveAsync()
            => Context.Client.DisconnectClientAsync(Chat.Simple("Is this what you wanted?"));

        [Command("uptime", "up")]
        [Description("Gets current uptime")]
        public Task UptimeAsync()
            => Context.Client.SendChatAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime).ToString()}");
    }
}
