using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// One block of a structure template, like vanilla's <c>StructureTemplate.StructureBlockInfo</c>.
/// </summary>
/// <param name="Position">Template-relative position before placement, world position while processing.</param>
/// <param name="Block">The block state.</param>
/// <param name="Nbt">Block entity data, or <c>null</c>.</param>
public readonly record struct StructureBlockInfo(Vector Position, IBlock Block, NbtCompound? Nbt);
