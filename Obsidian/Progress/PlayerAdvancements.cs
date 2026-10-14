using Obsidian.API.Advancements;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Obsidian.Progress;

/// <summary>Player-owned criteria and detached unknown JSON; never retains plugin trigger instances.</summary>
internal sealed class PlayerAdvancements(IPlayer player) : IAdvancements
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private JsonObject saved = new();
    private readonly HashSet<string> dirty = [];
    private WeakReference<FrozenDictionary<string, Advancement>>? sent;

    public bool IsDone(string identifier)
    {
        lock (this.gate)
            return AdvancementsRegistry.Current.TryGetValue(identifier, out var definition) && this.Done(definition);
    }

    public bool HasCriterion(string identifier, string criterion)
    {
        lock (this.gate)
            return this.Date(identifier, criterion) is not null;
    }

    public async ValueTask<bool> GrantAsync(string identifier, string? criterion = null)
    {
        Advancement definition;
        bool reward;
        lock (this.gate)
        {
            if (!AdvancementsRegistry.Current.TryGetValue(identifier, out definition))
                return false;

            var names = definition.Criteria.Where(value => criterion is null || value.Identifier == criterion)
                .Select(value => value.Identifier).ToArray();
            if (names.Length == 0)
                return false;

            var wasDone = this.Done(definition);
            var data = this.saved[identifier] as JsonObject;
            if (data is null)
            {
                data = new JsonObject { ["criteria"] = new JsonObject() };
                this.saved[identifier] = data;
            }

            var criteria = (JsonObject)data["criteria"]!;
            var changed = false;
            foreach (var name in names)
            {
                if (criteria[name] is not null)
                    continue;

                // Vanilla's AdvancementProgress date codec uses yyyy-MM-dd HH:mm:ss Z (+0000, not +00:00).
                var date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                criteria[name] = date.Remove(date.Length - 3, 1);
                changed = true;
            }

            if (!changed)
                return false;

            data["done"] = this.Done(definition);
            reward = !wasDone && this.Done(definition);
            this.dirty.Add(identifier);
        }

        if (reward && player is Entities.Player concrete)
            await concrete.RewardAdvancementAsync(definition);

        return true;
    }

    public bool Revoke(string identifier, string? criterion = null)
    {
        lock (this.gate)
        {
            if (!AdvancementsRegistry.Current.TryGetValue(identifier, out var definition)
                || this.saved[identifier]?["criteria"] is not JsonObject criteria)
            {
                return false;
            }

            var changed = false;
            foreach (var name in definition.Criteria.Select(value => value.Identifier))
            {
                if (criterion is null || criterion == name)
                    changed |= criteria.Remove(name);
            }

            if (changed)
            {
                this.saved[identifier]!["done"] = this.Done(definition);
                this.dirty.Add(identifier);
            }

            return changed;
        }
    }

    public async ValueTask FireAsync(string trigger, Func<JsonElement, bool> matches)
    {
        foreach (var advancement in AdvancementsRegistry.ForTrigger(trigger))
        {
            if (this.IsDone(advancement.Identifier))
                continue;

            foreach (var criterion in advancement.Criteria)
            {
                if (criterion.Trigger != trigger || this.HasCriterion(advancement.Identifier, criterion.Identifier))
                    continue;

                bool match;
                try
                {
                    match = matches(criterion.Conditions);
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException
                    or NotSupportedException or KeyNotFoundException or InvalidDataException)
                {
                    match = false;
                }

                if (match)
                    await this.GrantAsync(advancement.Identifier, criterion.Identifier);
            }
        }
    }

    public void Load(JsonObject json)
    {
        lock (this.gate)
        {
            this.saved = (JsonObject)json.DeepClone();
            this.saved.Remove("DataVersion");
            this.dirty.Clear();
            this.sent = null;
        }
    }

    public JsonObject Save()
    {
        lock (this.gate)
        {
            var result = (JsonObject)this.saved.DeepClone();
            foreach (var definition in AdvancementsRegistry.Current.Values)
            {
                if (result[definition.Identifier] is JsonObject entry)
                    entry["done"] = this.Done(definition);
            }

            result["DataVersion"] = VanillaLevelData.DataVersion;
            return result;
        }
    }

    /// <summary>Flushes on join and at the end of a tick; content replacement also resets the client's tree.</summary>
    public async ValueTask FlushAsync(bool initial = false)
    {
        await this.sending.WaitAsync();
        try
        {
            UpdateAdvancementsPacket packet;
            var definitions = AdvancementsRegistry.Current;
            lock (this.gate)
            {
                if (!initial && this.sent is null)
                    return;

                var reset = initial || !this.sent!.TryGetTarget(out var previous)
                    || !ReferenceEquals(previous, definitions);
                if (!reset && this.dirty.Count == 0)
                    return;

                packet = new UpdateAdvancementsPacket
                {
                    Reset = reset,
                    Added = reset ? definitions.Values.ToList() : [],
                    ShowAdvancements = !initial
                };
                var changed = reset ? definitions.Keys : this.dirty.AsEnumerable();
                foreach (var identifier in changed)
                {
                    if (definitions.TryGetValue(identifier, out var definition))
                    {
                        packet.Progress[identifier] = definition.Criteria.ToDictionary(
                            criterion => criterion.Identifier,
                            criterion => this.Date(identifier, criterion.Identifier));
                    }
                }

                this.dirty.Clear();
                this.sent = new(definitions);
            }

            await player.QueuePacketAsync(packet);
        }
        finally
        {
            this.sending.Release();
        }
    }

    private bool Done(Advancement definition) => definition.Requirements.Count > 0
        && definition.Requirements.All(row => row.Any(name => this.HasCriterion(definition.Identifier, name)));

    private DateTimeOffset? Date(string identifier, string name)
    {
        var value = this.saved[identifier]?["criteria"]?[name] as JsonValue;
        if (value is null || !value.TryGetValue<string>(out var text) || text.Length < 5)
            return null;

        var normalized = text.Insert(text.Length - 2, ":");
        return DateTimeOffset.TryParseExact(normalized, "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
}
