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
            var nbsFile = NbsFileReader.ReadNbsFile(song);

            int[] instruments = new int[]
            {
                106, //dirt, harp
                101, //wood, bass / double bass
                100,
                109, //sand, snare
                107, //glass, click / hat
                105, //wool, guitar
                104, //clay, flute
                102, //gold block, bell
                103, //packed ice, chime
                110, //bone block, xylophone
                //108, //???, pling
            };

            float[] pitches = new float[]
            {
                0.5f,
                0.529732f,
                0.561234f,
                0.594604f,
                0.629961f,
                0.667420f,
                0.707107f,
                0.749154f,
                0.793701f,
                0.840896f,
                0.890899f,
                0.943874f,
                1f,
                1.059463f,
                1.122462f,
                1.189207f,
                1.259921f,
                1.334840f,
                1.414214f,
                1.498307f,
                1.587401f,
                1.681793f,
                1.781797f,
                1.887749f,
                2f
            };

            NbsPlayerPluginClass.ServerTickStart = Context.Server.TotalTicks;

            Context.Server.Events.ServerTick += async () =>
            {
                int position = (Context.Server.TotalTicks - NbsPlayerPluginClass.ServerTickStart) / 1; //nbsFile.Tempo

                var noteBlocks = new List<NoteBlock>();

                foreach (NbsLayer layer in nbsFile.Layers)
                {
                    noteBlocks.AddRange(layer.NoteBlocks.FindAll(nb => nb.Tick == position));
                }

                var playerPosition = new Position((int)Context.Player.X, (int)Context.Player.Y, (int)Context.Player.Z);
                foreach (NoteBlock noteBlock in noteBlocks)
                {
                    float pitch = pitches[noteBlock.Key - 33];
                    float volume = 1f;//nbsFile.Layers[noteBlock.Layer].Volume / 100;
                    int instrument = instruments[noteBlock.Instrument];
                    await Context.Client.SendSoundEffectAsync(instrument, playerPosition, SoundCategory.Master, pitch, volume);
                }
            };

            await Context.Server.SendChatAsync($"Playing {nbsFile.SongAuthor} - {nbsFile.SongName}", Context.Client, 0, false);

            //TODO: implement your usual music playing stuff
        }

        [Command("test")]
        public async Task TestAsync(int soundId, float pitch)
        {
            var playerPosition = new Position((int)Context.Player.X, (int)Context.Player.Y, (int)Context.Player.Z);
            await Context.Client.SendSoundEffectAsync(soundId, playerPosition, SoundCategory.Master, pitch);
        }

       
    }
}