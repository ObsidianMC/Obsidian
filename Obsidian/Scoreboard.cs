using Obsidian.API;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian
{
    public class Scoreboard : IScoreboard
    {
        private readonly Server server;

        internal ScoreboardObjective objective;

        internal readonly Dictionary<string, IScore> scores = new Dictionary<string, IScore>();

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

                        foreach (var (_, score) in this.scores.OrderBy(x => x.Value.Value))
                        {
                            await player.client.QueuePacketAsync(new UpdateScore
                            {
                                EntityName = score.DisplayText,
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

                    foreach (var (_, score) in this.scores.OrderBy(x => x.Value.Value))
                    {
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = score.DisplayText,
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

        public async Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int value = 0)
        {
            var score = new Score
            {
                DisplayText = displayText,
                Value = value
            };

            if (this.scores.Count > 0)
            {
                score.Value = this.scores.Select(x => x.Value).OrderByDescending(x => x.Value).Last().Value;

                foreach (var element in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                    element.Value += 1;
            }

            this.scores[scoreName] = score;

            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                {
                    foreach (var (_, s) in this.scores.OrderBy(x => x.Value.Value))
                    {
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = s.DisplayText,
                            ObjectiveName = this.objective.ObjectiveName,
                            Action = 0,
                            Value = s.Value
                        });
                    }
                }
            }

        }

        public IScore GetScore(string scoreName) => this.scores.GetValueOrDefault(scoreName);
    }
}
