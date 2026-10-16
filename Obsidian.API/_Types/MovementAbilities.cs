namespace Obsidian.API;

/// <summary>
/// Ways a player can move beyond vanilla's rules, which mods grant with <see cref="IPlayer.SetMovementAbilities"/>.
/// The server and the Obsidian client's prediction both honour them, so a player using one isn't corrected.
/// </summary>
/// <remarks>
/// Attribute modifiers (<c>minecraft:jump_strength</c>, <c>minecraft:gravity</c>, <c>minecraft:movement_speed</c>,
/// <c>minecraft:safe_fall_distance</c> and the rest) tune the movement every player already has; these add moves that
/// no attribute gives.
/// </remarks>
public readonly record struct MovementAbilities
{
    /// <summary>
    /// How many more times the player can jump in the air, pressing jump again, before landing. Each one jumps like a
    /// ground jump, by the player's <c>minecraft:jump_strength</c>, and forgives the fall so far. Only the Obsidian
    /// client predicts them; other clients don't jump in the air.
    /// </summary>
    public int ExtraJumps { get; init; }

    /// <summary>
    /// Whether the player can glide like with an elytra, without wearing one: pressing jump in the air starts gliding,
    /// and landing stops it. Only the Obsidian client starts gliding without a glider item.
    /// </summary>
    public bool CanGlide { get; init; }

    /// <summary>
    /// Whether the player can fly like in creative mode, in any game mode: vanilla's <c>mayfly</c> ability, which
    /// every client honours.
    /// </summary>
    public bool CanFly { get; init; }

    /// <summary>
    /// Both grants together: their extra jumps added up, and each ability if either has it.
    /// </summary>
    public MovementAbilities Combine(MovementAbilities other) => new()
    {
        ExtraJumps = this.ExtraJumps + other.ExtraJumps,
        CanGlide = this.CanGlide || other.CanGlide,
        CanFly = this.CanFly || other.CanFly
    };
}
