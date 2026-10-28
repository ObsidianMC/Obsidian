using Obsidian.Nbt;

namespace Obsidian.API.World;

/// <summary>A detached block state and saved block entity data, suitable for copying or undoing edits.</summary>
public sealed record BlockSnapshot(IBlock Block, NbtCompound? Data = null);
