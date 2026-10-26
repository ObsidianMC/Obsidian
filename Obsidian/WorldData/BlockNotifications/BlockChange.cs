namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// A block change that is already committed: <see cref="New"/> is what the world reads now, and <see cref="Old"/> is what it replaced.
/// </summary>
internal readonly record struct BlockChange(Vector Position, IBlock Old, IBlock New);
