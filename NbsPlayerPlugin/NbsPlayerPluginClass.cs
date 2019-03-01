using Obsidian;
using Obsidian.Entities;
using Obsidian.Plugins;
using System;
using System.Collections.Generic;

namespace NbsPlayerPlugin
{
    public class NbsPlayerPluginClass : IPluginClass
    {
        private Server server;

        public static List<PlayerTask> Tasks = new List<PlayerTask>();

        public PluginInfo Initialize(Server server)
        {
            this.server = server;

            this.server.Commands.AddModule<NbsPlayerCommands>();

            this.server.Events.ServerTick += async () =>
            {
                foreach (PlayerTask task in Tasks)
                {
                    task.Position = (int)((this.server.TotalTicks - task.TickStart) + task.NBS.Delay) / 2;

                    await task.Client.SendChatAsync("position: " + task.Position, 2);

                    var noteBlocks = new List<NoteBlock>();

                    foreach (NbsLayer layer in task.NBS.Layers)
                    {
                        noteBlocks.AddRange(layer.NoteBlocks.FindAll(nb => nb.Tick == task.Position));
                    }

                    Location location = task.Client.Player.Location;
                    foreach (NoteBlock noteBlock in noteBlocks)
                    {
                        float pitch = Constants.PitchValues[noteBlock.Key - 33];
                        float volume = task.NBS.Layers[noteBlock.Layer].Volume / 100;
                        int instrument = Constants.InstrumentValues[noteBlock.Instrument];
                        var playerPosition = new Position((int)location.X, (int)location.Y, (int)location.Z);
                        await task.Client.SendSoundEffectAsync(instrument, playerPosition, SoundCategory.Records, pitch, volume);
                    }
                }
            };

            return new PluginInfo(
                "NBS Player Plugin",
                "Craftplacer (Obsidian Team)",
                "0.1",
                "Plays back .NBS files stored on this server",
                "https://github.com/NaamloosDT/Obsidian"
            );
        }
    }
}