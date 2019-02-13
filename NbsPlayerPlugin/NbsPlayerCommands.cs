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
        public async Task SampleCommandAsync(string song)
        {
            var nbsFile = NbsFileReader.ReadNbsFile(song);

            const int basedrum = 100;
            const int bass = 101;
            const int bell = 102;
            const int chime = 103;
            const int flute = 104;
            const int guitar = 105;
            const int harp = 106;
            const int hat = 107;
            const int pling = 108;
            const int snare = 109;
            const int xylophone = 110;

            NbsPlayerPluginClass.ServerTickStart = Context.Server.TotalTicks;

            Context.Server.Events.ServerTick += async () =>
            {
                var position = (Context.Server.TotalTicks - NbsPlayerPluginClass.ServerTickStart) / nbsFile.Tempo;

                var noteBlocks = new List<NoteBlock>();

                foreach (NbsLayer layer in nbsFile.Layers)
                {
                    noteBlocks.AddRange(layer.NoteBlocks.FindAll(nb => nb.Tick == position));
                }

                var playerPosition = new Position((int)Context.Player.X, (int)Context.Player.Y, (int)Context.Player.Z);
                foreach (NoteBlock noteBlock in noteBlocks)
                {

                    float minecraftPitch = 0.5f + (((noteBlock.Key) / 25) * 1.5f);
                    await Context.Client.SendSoundEffectAsync(harp, playerPosition, SoundCategory.Master, minecraftPitch, nbsFile.Layers[noteBlock.Layer].Volume);
                }
            };

            await Context.Server.SendChatAsync($"Playing {nbsFile.SongAuthor} - {nbsFile.SongName}", Context.Client, 0, false);

            //TODO: implement your usual music playing stuff
        }
    }
}