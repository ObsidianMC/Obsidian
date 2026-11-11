namespace Obsidian.API.Commands;

/// <summary>
/// A sender with a permission level of its own rather than a player's or the console's, like vanilla's command block
/// sources, which run at the game master level (2) whatever their issuer.
/// </summary>
/// <remarks>
/// The permission checks read <see cref="PermissionLevel"/> before treating a console issuer as allowed everything, so
/// a command block can't run what only operators of level 3 and 4 (or the console) may.
/// </remarks>
internal interface IPermissionLevelSender
{
    /// <summary>The sender's permission level, from 0 to 4.</summary>
    int PermissionLevel { get; }
}
