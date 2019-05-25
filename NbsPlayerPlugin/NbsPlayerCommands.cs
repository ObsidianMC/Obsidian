using Obsidian.Commands;
using Obsidian.Entities;
using Qmmands;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NbsPlayerPlugin
{
    public class NbsPlayerCommands : ModuleBase<CommandContext>
    {
        public CommandService Service { get; set; }

        [Command("play")]
        [Description("Plays back the specified song.")]
        public async Task PlayAsync(string song)
        {
            NbsFile nbsFile = NbsFileReader.ReadNbsFile(song);

            bool songNameSpecified = !string.IsNullOrWhiteSpace(nbsFile.SongName);
            bool songAuthorSpecified = !string.IsNullOrWhiteSpace(nbsFile.OriginalSongAuthor);

            if (songNameSpecified || songAuthorSpecified)
            {
                string songName = songNameSpecified ? "Unknown" : nbsFile.SongName;
                string songAuthor = songAuthorSpecified  ? "Unknown" : nbsFile.OriginalSongAuthor;
                await Context.Client.SendChatAsync($"{Constants.Prefix} Playing {songAuthor} - {songName}");
            }
            else
            {
                await Context.Client.SendChatAsync($"{Constants.Prefix} Playing {song}");
            }
            
            NbsPlayerPluginClass.Tasks.Add(new PlayerTask(nbsFile, Context.Client, Context.Server.TotalTicks));
        }

        [Command("stop")]
        [Description("Stops the currently playing song")]
        public async Task StopAsync()
        {
            try
            {
                NbsPlayerPluginClass.StopTask(Context.Player.Username);
                await Context.Client.SendChatAsync($"{Constants.Prefix} Stopped playing");
            }
            catch
            {
                await Context.Client.SendChatAsync($"{Constants.Prefix} You aren't playing a song right now.");
            }
        }
    }
}