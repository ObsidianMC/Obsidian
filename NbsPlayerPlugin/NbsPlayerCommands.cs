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

            string songName = string.IsNullOrWhiteSpace(nbsFile.SongName) ? "Unknown" : nbsFile.SongName;
            string songAuthor = string.IsNullOrWhiteSpace(nbsFile.OriginalSongAuthor) ? "Unknown" : nbsFile.OriginalSongAuthor;

            await Context.Client.SendChatAsync($"{Constants.Prefix} Playing {songAuthor} - {songName}");
            var task = new PlayerTask(nbsFile, Context.Client, Context.Server.TotalTicks);

            NbsPlayerPluginClass.Tasks.Add(task);
        }

        [Command("test")]
        public async Task TestAsync(int soundId, float pitch)
        {
            var loc = Context.Player.Location;
            await Context.Client.SendSoundEffectAsync(soundId, new Position((int)loc.X, (int)loc.Y, (int)loc.Z), SoundCategory.Master, pitch);
        }
    }
}