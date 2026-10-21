using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;
using System.Threading;

namespace Obsidian;

/// <summary>
/// The scoreboard <c>/scoreboard</c> manages, as vanilla's: objectives, each holder's score for them, and which
/// objectives the display slots show. Every online player sees it; players who join are sent it.
/// </summary>
/// <remarks>
/// It lives as long as the server and isn't saved. Objectives keep their criteria, but only <c>dummy</c> scores are
/// meaningful: nothing updates the others yet.
/// </remarks>
internal sealed class VanillaScoreboard(IServer server)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Objective> objectives = new(StringComparer.Ordinal);
    private readonly Dictionary<DisplaySlot, string> displayed = [];

    public sealed class Objective(string name, string criteria, ChatMessage displayName)
    {
        public string Name { get; } = name;
        public string Criteria { get; } = criteria;
        public ChatMessage DisplayName { get; set; } = displayName;
        public DisplayType RenderType { get; set; } = DisplayType.Integer;
        public Dictionary<string, int> Scores { get; } = new(StringComparer.Ordinal);
    }

    public IReadOnlyList<Objective> Objectives
    {
        get
        {
            lock (this.gate)
                return [.. this.objectives.Values];
        }
    }

    /// <summary>Every holder with a score for some objective.</summary>
    public IReadOnlyList<string> Holders
    {
        get
        {
            lock (this.gate)
                return [.. this.objectives.Values.SelectMany(objective => objective.Scores.Keys).Distinct()];
        }
    }

    public Objective? Find(string name)
    {
        lock (this.gate)
            return this.objectives.GetValueOrDefault(name);
    }

    public bool TryAdd(string name, string criteria, ChatMessage displayName)
    {
        lock (this.gate)
        {
            if (!this.objectives.TryAdd(name, new Objective(name, criteria, displayName)))
                return false;

            this.Broadcast(CreatePacket(this.objectives[name], ScoreboardMode.Create));
            return true;
        }
    }

    public bool Remove(string name)
    {
        lock (this.gate)
        {
            if (!this.objectives.Remove(name))
                return false;

            foreach (var slot in this.displayed.Where(x => x.Value == name).Select(x => x.Key).ToArray())
                this.displayed.Remove(slot);

            this.Broadcast(new SetObjectivePacket { ObjectiveName = name, Mode = ScoreboardMode.Remove });
            return true;
        }
    }

    public void Update(Objective objective)
    {
        lock (this.gate)
            this.Broadcast(CreatePacket(objective, ScoreboardMode.Update));
    }

    /// <summary>Shows an objective in a display slot, or clears the slot with <see langword="null"/>.</summary>
    public void SetDisplay(DisplaySlot slot, string? objective)
    {
        lock (this.gate)
        {
            if (objective is null)
                this.displayed.Remove(slot);
            else
                this.displayed[slot] = objective;

            this.Broadcast(new SetDisplayObjectivePacket { DisplaySlot = slot, ObjectiveName = objective ?? string.Empty });
        }
    }

    public int? GetScore(string holder, Objective objective)
    {
        lock (this.gate)
            return objective.Scores.TryGetValue(holder, out var score) ? score : null;
    }

    public void SetScore(string holder, Objective objective, int value)
    {
        lock (this.gate)
        {
            objective.Scores[holder] = value;
            this.Broadcast(new SetScorePacket { EntityName = holder, ObjectiveName = objective.Name, Value = value });
        }
    }

    /// <summary>Removes a holder's score for one objective, or all of them.</summary>
    public bool Reset(string holder, Objective? objective)
    {
        lock (this.gate)
        {
            var removed = objective is null
                ? this.objectives.Values.Count(x => x.Scores.Remove(holder)) > 0
                : objective.Scores.Remove(holder);

            if (removed)
                this.Broadcast(new ResetScorePacket { EntityName = holder, ObjectiveName = objective?.Name });

            return removed;
        }
    }

    /// <summary>Sends the whole scoreboard to a player who joined.</summary>
    public void SendTo(IPlayer player)
    {
        lock (this.gate)
        {
            foreach (var objective in this.objectives.Values)
            {
                player.SendPacket(CreatePacket(objective, ScoreboardMode.Create));

                foreach (var (holder, value) in objective.Scores)
                    player.SendPacket(new SetScorePacket { EntityName = holder, ObjectiveName = objective.Name, Value = value });
            }

            foreach (var (slot, objective) in this.displayed)
                player.SendPacket(new SetDisplayObjectivePacket { DisplaySlot = slot, ObjectiveName = objective });
        }
    }

    private static SetObjectivePacket CreatePacket(Objective objective, ScoreboardMode mode) => new()
    {
        ObjectiveName = objective.Name,
        Mode = mode,
        Value = objective.DisplayName,
        Type = objective.RenderType
    };

    private void Broadcast(IClientboundPacket packet)
    {
        foreach (var player in server.OnlinePlayers.Values)
            player.SendPacket(packet);
    }
}
