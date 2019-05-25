using Obsidian;
using Obsidian.Entities;
using Obsidian.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NbsPlayerPlugin
{
    public class NbsPlayerPluginClass : IPluginClass
    {
        private Server server;

        public static List<PlayerTask> Tasks = new List<PlayerTask>();

        public static void StopTask(string username) => Tasks.Remove(Tasks.First(t => t.Client.Player.Username == username));

        public PluginInfo Initialize(Server server)
        {
            this.server = server;

            this.server.Commands.AddModule<NbsPlayerCommands>();

            this.server.Events.ServerTick += async () =>
            {
                //Credit: https://stackoverflow.com/questions/17767161/possible-to-modify-a-list-while-iterating-through-it
                for (int i = Tasks.Count - 1; i >= 0; i--)
                {
                    var task = Tasks[i];

                    try
                    {
                        task.Position = (int)((this.server.TotalTicks - task.TickStart) + task.NBS.Delay) / 2;

                        //await task.Client.SendChatAsync("position: " + task.Position, 2);

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
                    catch
                    {
                        Tasks.RemoveAt(i);
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