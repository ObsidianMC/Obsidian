using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// A trial spawner's block entity, like vanilla's <c>TrialSpawnerBlockEntity</c>: its configs, and how far its trial
/// has got, which its tick moves on (see the other part of this class).
/// </summary>
/// <remarks>
/// Saved as vanilla saves it, each field left out at its default: <c>normal_config</c> and <c>ominous_config</c> (a
/// registry entry's id, or a config written out), <c>target_cooldown_length</c>,
/// <c>required_player_range</c>, <c>registered_players</c>, <c>current_mobs</c>, <c>cooldown_ends_at</c>,
/// <c>next_mob_spawns_at</c>, <c>total_mobs_spawned</c>, <c>spawn_data</c> and <c>ejecting_loot_table</c>. Other
/// fields are kept as loaded. The saved data is an immutable snapshot the tick replaces, so chunks save it from any
/// thread.
/// </remarks>
internal sealed partial class TrialSpawnerBlockEntity : BlockEntity
{
    // Vanilla's FullConfig defaults.
    private const int DefaultCooldownLength = 36000;
    private const int DefaultPlayerRange = 14;

    private static readonly string[] fullConfigFields =
        ["normal_config", "ominous_config", "target_cooldown_length", "required_player_range"];

    private static readonly string[] stateFields =
    [
        "registered_players", "current_mobs", "cooldown_ends_at", "next_mob_spawns_at", "total_mobs_spawned",
        "spawn_data", "ejecting_loot_table"
    ];

    private TrialSpawnerData data = TrialSpawnerData.Empty;

    // The block's state as the tick last saw it, which decides what clients get.
    private int state;

    internal override bool SavesOnAnyThread => true;

    /// <summary>The saved data as of the last tick or load.</summary>
    public TrialSpawnerData Data
    {
        get => Volatile.Read(ref this.data);
        private set => Volatile.Write(ref this.data, value);
    }

    private TrialSpawnerState State
    {
        get => (TrialSpawnerState)Volatile.Read(ref this.state);
        set => Volatile.Write(ref this.state, (int)value);
    }

    protected internal override void Load(NbtCompound tag)
    {
        var others = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (!fullConfigFields.Contains(name) && !stateFields.Contains(name))
                others.Add(name, DataBlockEntity.Copy(child));
        }

        var loaded = TrialSpawnerData.Empty with
        {
            RegisteredPlayers = UuidSet.Of(SavedUuids.Read(tag, "registered_players")),
            CurrentMobs = UuidSet.Of(SavedUuids.Read(tag, "current_mobs")),
            CooldownEndsAt = tag.TryGetNumber<long>("cooldown_ends_at", out var cooldown) ? cooldown : 0,
            NextMobSpawnsAt = tag.TryGetNumber<long>("next_mob_spawns_at", out var next) ? next : 0,
            TotalMobsSpawned = tag.TryGetNumber<int>("total_mobs_spawned", out var total) && total >= 0 ? total : 0,
            NextSpawnData = tag.TryGetTag<NbtCompound>("spawn_data", out var spawnData)
                && spawnData.TryGetTag<NbtCompound>("entity", out _)
                    ? DataBlockEntity.Copy(spawnData)
                    : null,
            EjectingLootTable = tag.TryGetTagValue<string>("ejecting_loot_table", out var table) ? table : null
        };

        this.Data = ReadFullConfig(tag, loaded, others) with { Others = others };
    }

    // Vanilla's FullConfig, from its codec's partial result: each of the four fields that doesn't read takes its
    // default on its own. A config naming a registry entry this server doesn't have is kept as it was and saved again,
    // where vanilla drops it, since it may be a data pack's; the spawner uses the default config meanwhile.
    private static TrialSpawnerData ReadFullConfig(NbtCompound tag, TrialSpawnerData loaded, NbtCompound others)
    {
        var (normalTag, normalConfig) = ReadConfig(tag, "normal_config", others);
        var (ominousTag, ominousConfig) = ReadConfig(tag, "ominous_config", others);
        var cooldown = tag.TryGetNumber<int>("target_cooldown_length", out var length) && length >= 0
            ? length
            : DefaultCooldownLength;
        var range = tag.TryGetNumber<int>("required_player_range", out var required) && required is >= 1 and <= 128
            ? required
            : DefaultPlayerRange;

        return loaded with
        {
            NormalConfigTag = normalTag,
            OminousConfigTag = ominousTag,
            NormalConfig = normalConfig,
            OminousConfig = ominousConfig,
            TargetCooldownLength = cooldown,
            RequiredPlayerRange = range
        };
    }

    private static (INbtTag? Tag, TrialSpawnerConfig Config) ReadConfig(
        NbtCompound tag,
        string name,
        NbtCompound others)
    {
        if (!tag.TryGetTag(name, out var saved))
            return (null, TrialSpawnerConfig.Default);

        if (TrialSpawnerConfig.Read(saved) is TrialSpawnerConfig config)
            return (DataBlockEntity.Copy(saved), config);

        if (saved is NbtTag<string>)
            others.Add(name, DataBlockEntity.Copy(saved));

        return (null, TrialSpawnerConfig.Default);
    }

    protected internal override void Save(NbtCompound tag)
    {
        var saved = this.Data;
        foreach (var (name, child) in saved.Others)
            tag.Add(name, DataBlockEntity.Copy(child));

        SavedUuids.Write(tag, "registered_players", saved.RegisteredPlayers.Members);
        SavedUuids.Write(tag, "current_mobs", saved.CurrentMobs.Members);
        if (saved.CooldownEndsAt != 0)
            tag.Add(new NbtTag<long>("cooldown_ends_at", saved.CooldownEndsAt));

        if (saved.NextMobSpawnsAt != 0)
            tag.Add(new NbtTag<long>("next_mob_spawns_at", saved.NextMobSpawnsAt));

        if (saved.TotalMobsSpawned != 0)
            tag.Add(new NbtTag<int>("total_mobs_spawned", saved.TotalMobsSpawned));

        if (saved.NextSpawnData is NbtCompound spawnData)
            tag.Add("spawn_data", DataBlockEntity.Copy(spawnData));

        if (saved.EjectingLootTable is string table)
            tag.Add(new NbtTag<string>("ejecting_loot_table", table));

        WriteConfig(tag, "normal_config", saved.NormalConfigTag, saved.NormalConfig);
        WriteConfig(tag, "ominous_config", saved.OminousConfigTag, saved.OminousConfig);

        if (saved.TargetCooldownLength != DefaultCooldownLength)
            tag.Add(new NbtTag<int>("target_cooldown_length", saved.TargetCooldownLength));

        if (saved.RequiredPlayerRange != DefaultPlayerRange)
            tag.Add(new NbtTag<int>("required_player_range", saved.RequiredPlayerRange));
    }

    // Vanilla writes a registry entry's id as it was, and a config written out inline again from what it read, which
    // leaves out each field at its default and the whole config when it's the default.
    private static void WriteConfig(NbtCompound tag, string name, INbtTag? saved, TrialSpawnerConfig config)
    {
        if (saved is NbtTag<string> id)
            tag.Add(name, DataBlockEntity.Copy(id));
        else if (saved is not null && !config.IsDefault)
            tag.Add(config.Write(name));
    }

    /// <summary>
    /// What clients get, like vanilla's <c>TrialSpawnerStateData.getUpdateTag</c>: when the next mob spawns while the
    /// spawner is active, and the spawn data, whose mob they show spinning inside. Empty, but still sent, without
    /// either.
    /// </summary>
    protected internal override NbtCompound? GetUpdateData()
    {
        var saved = this.Data;
        var update = new NbtCompound();
        if (this.State == TrialSpawnerState.Active)
            update.Add(new NbtTag<long>("next_mob_spawns_at", saved.NextMobSpawnsAt));

        if (saved.NextSpawnData is NbtCompound spawnData)
            update.Add("spawn_data", DataBlockEntity.Copy(spawnData));

        return update;
    }
}

/// <summary>
/// What a trial spawner saves: its configs and its trial's progress, like vanilla's <c>FullConfig</c> and
/// <c>TrialSpawnerStateData</c>. Immutable; the tick replaces it.
/// </summary>
internal sealed record TrialSpawnerData
{
    public static TrialSpawnerData Empty { get; } = new();

    /// <summary>The saved <c>normal_config</c> as loaded, or null for the default config.</summary>
    public INbtTag? NormalConfigTag { get; init; }

    /// <summary>The saved <c>ominous_config</c> as loaded, or null for the default config.</summary>
    public INbtTag? OminousConfigTag { get; init; }

    public TrialSpawnerConfig NormalConfig { get; init; } = TrialSpawnerConfig.Default;

    public TrialSpawnerConfig OminousConfig { get; init; } = TrialSpawnerConfig.Default;

    /// <summary>How long a cooldown lasts, counted from when the trial's last mob went.</summary>
    public int TargetCooldownLength { get; init; } = 36000;

    /// <summary>How close a player must be for the spawner to detect them.</summary>
    public int RequiredPlayerRange { get; init; } = 14;

    /// <summary>The players taking part, saved in the order they were detected.</summary>
    public UuidSet RegisteredPlayers { get; init; } = UuidSet.Empty;

    /// <summary>The mobs the spawner tracks, saved in the order they spawned.</summary>
    public UuidSet CurrentMobs { get; init; } = UuidSet.Empty;

    /// <summary>The game time the cooldown ends at.</summary>
    public long CooldownEndsAt { get; init; }

    /// <summary>The game time from which the next mob may spawn.</summary>
    public long NextMobSpawnsAt { get; init; }

    public int TotalMobsSpawned { get; init; }

    /// <summary>
    /// The next mob's spawn data (vanilla's <c>SpawnData</c>, saved form), or null before it's picked.
    /// </summary>
    public NbtCompound? NextSpawnData { get; init; }

    /// <summary>The loot table this ejection of rewards rolls, once picked.</summary>
    public string? EjectingLootTable { get; init; }

    /// <summary>The saved fields the spawner doesn't read, kept as loaded.</summary>
    public NbtCompound Others { get; init; } = new();
}
