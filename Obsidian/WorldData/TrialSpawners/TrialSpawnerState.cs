namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// Where a trial spawner is in its trial, like vanilla's <c>TrialSpawnerState</c>: its block's
/// <c>trial_spawner_state</c> property.
/// </summary>
internal enum TrialSpawnerState
{
    /// <summary>Nothing to spawn.</summary>
    Inactive,

    /// <summary>Waiting for a player to come in sight.</summary>
    WaitingForPlayers,

    /// <summary>Spawning the trial's mobs.</summary>
    Active,

    /// <summary>Every mob of the trial is gone; the shutter opens soon.</summary>
    WaitingForRewardEjection,

    /// <summary>Ejecting a reward for each player of the trial.</summary>
    EjectingReward,

    /// <summary>Cooling down until the next trial.</summary>
    Cooldown
}

internal static class TrialSpawnerStates
{
    public const string Property = "trial_spawner_state";

    /// <summary>The state's value of the block property.</summary>
    public static string Name(this TrialSpawnerState state) => state switch
    {
        TrialSpawnerState.Inactive => "inactive",
        TrialSpawnerState.WaitingForPlayers => "waiting_for_players",
        TrialSpawnerState.Active => "active",
        TrialSpawnerState.WaitingForRewardEjection => "waiting_for_reward_ejection",
        TrialSpawnerState.EjectingReward => "ejecting_reward",
        _ => "cooldown"
    };

    /// <summary>The state a trial spawner block is in; inactive for any other block, as vanilla reads it.</summary>
    public static TrialSpawnerState Of(IBlock block) => block.GetProperty(Property) switch
    {
        "waiting_for_players" => TrialSpawnerState.WaitingForPlayers,
        "active" => TrialSpawnerState.Active,
        "waiting_for_reward_ejection" => TrialSpawnerState.WaitingForRewardEjection,
        "ejecting_reward" => TrialSpawnerState.EjectingReward,
        "cooldown" => TrialSpawnerState.Cooldown,
        _ => TrialSpawnerState.Inactive
    };

    /// <summary>Whether the client shows the spawn data's mob spinning inside, which needs one to load.</summary>
    public static bool HasSpinningMob(this TrialSpawnerState state) =>
        state is TrialSpawnerState.WaitingForPlayers or TrialSpawnerState.Active;
}
