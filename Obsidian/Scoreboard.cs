using Obsidian.API;
using Obsidian.Net.Packets.Play.Clientbound;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian
{
    public class Scoreboard : IScoreboard
    {
        internal ScoreboardObjective objective;

        internal readonly Dictionary<string, IScore> scores = new Dictionary<string, IScore>();

        private readonly Server server;

        internal string Name { get; set; }

        public Scoreboard(Server server)
        {
            this.server = server;
        }

        public async Task CreateOrUpdateObjectiveAsync(IChatMessage title, DisplayType displayType = DisplayType.Integer)
        {
            var obj = new ScoreboardObjectivePacket
            {
                ObjectiveName = this.Name,
                Mode = this.objective != null ? ScoreboardMode.Update : ScoreboardMode.Create,
                Value = title,
                Type = displayType
            };

            if (this.objective != null)
            {
                foreach (var (_, player) in this.server.OnlinePlayers)
                {
                    if (player.CurrentScoreboard == this)
                    {
                        await player.client.QueuePacketAsync(obj);

                        foreach (var (name, score) in this.scores)
                        {
                            await player.client.QueuePacketAsync(new UpdateScore
                            {
                                EntityName = name,
                                ObjectiveName = this.objective.ObjectiveName,
                                Action = 0,
                                Value = score.Value
                            });
                        }
                    }
                }

                return;
            }

            this.objective = new ScoreboardObjective
            {
                ObjectiveName = this.Name,
                Value = title,
                DisplayType = displayType
            };

            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                {
                    await player.client.QueuePacketAsync(obj);

                    foreach (var (name, score) in this.scores)
                    {
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = name,
                            ObjectiveName = this.objective.ObjectiveName,
                            Action = 0,
                            Value = score.Value
                        });
                    }
                }
            }
        }

        public async Task RemoveObjectiveAsync()
        {
            var obj = new ScoreboardObjectivePacket
            {
                ObjectiveName = this.Name,
                Mode = ScoreboardMode.Remove
            };

            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                    await player.client.QueuePacketAsync(obj);
            }
        }

        public async Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int score = 0)
        {
            
        }
    }
}
