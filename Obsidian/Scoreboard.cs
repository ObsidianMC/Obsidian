using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian
{
    public class Scoreboard : IScoreboard
    {
        private readonly Server server;

        internal readonly Dictionary<string, Score> scores = new Dictionary<string, Score>();

        internal string Name { get; set; }

        public ScoreboardObjective Objective;

        public Scoreboard(Server server)
        {
            this.server = server;
        }

        public async Task CreateOrUpdateObjectiveAsync(IChatMessage title, DisplayType displayType = DisplayType.Integer)
        {
            var obj = new ScoreboardObjectivePacket
            {
                ObjectiveName = this.Name,
                Mode = this.Objective != null ? ScoreboardMode.Update : ScoreboardMode.Create,
                Value = title,
                Type = displayType
            };

            if (this.Objective != null)
            {
                foreach (var (_, player) in this.server.OnlinePlayers)
                {
                    if (player.CurrentScoreboard == this)
                    {
                        await player.client.QueuePacketAsync(obj);

                        foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                        {
                            await player.client.QueuePacketAsync(new UpdateScore
                            {
                                EntityName = score.DisplayText,
                                ObjectiveName = this.Name,
                                Action = 0,
                                Value = score.Value
                            });
                        }
                    }
                }

                return;
            }

            this.Objective = new ScoreboardObjective
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

                    foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                    {
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = score.DisplayText,
                            ObjectiveName = this.Name,
                            Action = 0,
                            Value = score.Value
                        });
                    }
                }
            }
        }

        public Task CreateOrUpdateObjectiveAsync(string title, DisplayType displayType = DisplayType.Integer) 
            => this.CreateOrUpdateObjectiveAsync(ChatMessage.Simple(title), displayType);

        public async Task RemoveObjectiveAsync()
        {
            var obj = new ScoreboardObjectivePacket
            {
                ObjectiveName = this.Objective.ObjectiveName,
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
                            ObjectiveName = this.Objective.ObjectiveName,
                            Action = 0,
                            Value = s.Value
                        });
                    }
                }
            }

        }

        public Score GetScore(string scoreName) => this.scores.GetValueOrDefault(scoreName);
    }
}
