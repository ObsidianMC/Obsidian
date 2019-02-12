using Obsidian;
using Obsidian.Commands;
using Obsidian.Plugins;
using Qmmands;
using System.Threading.Tasks;

namespace NbsPlayerPlugin
{
    public class NbsPlayerPluginClass : IPluginClass
    {
        Server server;

        public PluginInfo Initialize(Server server)
        {
            this.server = server;

            this.server.Commands.AddModule<NbsPlayerCommands>();

            return new PluginInfo(
                "NBS Player Plugin",
                "Craftplacer (Obsidian Team)",
                "0.1",
                "Plays back .NBS files stored on this server",
                "https://github.com/NaamloosDT/Obsidian"
            );
        }
    }

    public class NbsPlayerCommands : ModuleBase<CommandContext>
    {
        public CommandService Service { get; set; }

        [Command("play")]
        [Description("Plays back the specified song.")]
        public async Task SampleCommandAsync(string song)
        {
            var nbsFile = NbsFileReader.ReadNbsFile(song);
            

            await Context.Server.SendChatAsync($"Playing {nbsFile.SongAuthor} - {nbsFile.SongName}", Context.Client, 0, false);

            //TODO: implement your usual music playing stuff
        }
    }
}
