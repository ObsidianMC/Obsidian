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

        internal readonly string name;

        internal readonly Dictionary<string, Score> scores = new Dictionary<string, Score>();

        public ScoreboardObjective Objective { get; private set; }

        public Scoreboard(string name, Server server)
        {
            this.name = name;
            this.server = server;
        }

        public async Task CreateOrUpdateObjectiveAsync(ChatMessage title, DisplayType displayType = DisplayType.Integer)
        {
            var packet = new ScoreboardObjectivePacket
            {
                ObjectiveName = this.name,
                Mode = this.Objective != null ? ScoreboardMode.Update : ScoreboardMode.Create,
                Value = title,
                Type = displayType
            };

            if (this.Objective != null)
            {
                await this.UpdateObjectiveAsync(packet);
            }
            else
            {
                this.Objective = new ScoreboardObjective
                {
                    ObjectiveName = this.name,
                    Value = title,
                    DisplayType = displayType
                };

                foreach (var (_, player) in this.server.OnlinePlayers)
                {
                    if (player.CurrentScoreboard == this)
                    {
                        await player.client.QueuePacketAsync(packet);

                        foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                        {
                            await player.client.QueuePacketAsync(new UpdateScore
                            {
                                EntityName = score.DisplayText,
                                ObjectiveName = this.name,
                                Action = 0,
                                Value = score.Value
                            });
                        }
                    }
                }
            }
        }

        public Task CreateOrUpdateObjectiveAsync(string title, DisplayType displayType = DisplayType.Integer)
            => this.CreateOrUpdateObjectiveAsync(ChatMessage.Simple(title), displayType);

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

            Score previousScore = null;
            if (this.scores.ContainsKey(scoreName))
                previousScore = this.scores[scoreName];

            this.scores[scoreName] = score;

            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                {
                    foreach (var (_, s) in this.scores.OrderBy(x => x.Value.Value))
                    {
                        if (previousScore is not null)
                        {
                            await player.client.QueuePacketAsync(new UpdateScore
                            {
                                EntityName = previousScore.DisplayText,
                                ObjectiveName = this.Objective.ObjectiveName,
                                Action = 1,
                                Value = s.Value
                            });
                        }
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

        public async Task RemoveScoreAsync(string scoreName)
        {
            if (this.scores.ContainsKey(scoreName))
            {
                foreach (var (_, player) in this.server.OnlinePlayers)
                {
                    if (player.CurrentScoreboard == this)
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = this.scores[scoreName].DisplayText,
                            ObjectiveName = this.Objective.ObjectiveName,
                            Action = 1,
                            Value = this.scores[scoreName].Value
                        });
                }
            }
        }

        public Score GetScore(string scoreName) => this.scores.GetValueOrDefault(scoreName);

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

        private async Task UpdateObjectiveAsync(ScoreboardObjectivePacket packet)
        {
            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                {
                    await player.client.QueuePacketAsync(packet);

                    foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                    {
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = score.DisplayText,
                            ObjectiveName = this.name,
                            Action = 0,
                            Value = score.Value
                        });
                    }
                }
            }
        }
    }
}
