namespace Obsidian.WorldData.Features.Tree.Placers;

/// <summary>
/// Helper methods for trunk placer implementations.
/// </summary>
public static class TrunkPlacerHelper
{
	/// <summary>
	/// Sets dirt at the specified position if needed (based on ForceDirt flag or existing block).
	/// </summary>
	public static async ValueTask SetDirtAt(IWorld world, Vector pos, IBlock dirtBlock, bool forceDirt = false)
	{
		if (forceDirt || !await IsDirt(world, pos))
		{
			await world.SetBlockUntrackedAsync(pos, dirtBlock, false);
		}
	}

	/// <summary>
	/// Places a log block at the specified position if valid for tree placement.
	/// </summary>
	public static async ValueTask<bool> PlaceLog(IWorld world, Vector pos, IBlock trunkBlock)
	{
		if (await ValidTreePos(world, pos))
		{
			await world.SetBlockUntrackedAsync(pos, trunkBlock, false);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Places a log block only if the position is free (air/replaceable or existing log).
	/// </summary>
	public static async ValueTask PlaceLogIfFree(IWorld world, Vector pos, IBlock trunkBlock)
	{
		if (await IsFree(world, pos))
		{
			await PlaceLog(world, pos, trunkBlock);
		}
	}

	/// <summary>
	/// Checks if a position is valid for tree placement (replaceable blocks like air, grass, etc.).
	/// </summary>
	public static async ValueTask<bool> ValidTreePos(IWorld world, Vector pos)
	{
		var block = await world.GetBlockAsync(pos);
		if (block == null)
			return false;

		// Check if block is in the replaceable tag
		return TagsRegistry.Block.Replaceable.Entries.Contains(block.RegistryId);
	}

	/// <summary>
	/// Checks if a position is free for placement (replaceable or existing log).
	/// </summary>
	public static async ValueTask<bool> IsFree(IWorld world, Vector pos)
	{
		if (await ValidTreePos(world, pos))
			return true;

		var block = await world.GetBlockAsync(pos);
		if (block == null)
			return false;

		// Check if block is a log (can grow through existing logs)
		return TagsRegistry.Block.Logs.Entries.Contains(block.RegistryId);
	}

	/// <summary>
	/// Checks if a block at the specified position is dirt (but not grass or mycelium).
	/// Used to determine if dirt should be placed below tree origin.
	/// </summary>
	private static async ValueTask<bool> IsDirt(IWorld world, Vector pos)
	{
		var block = await world.GetBlockAsync(pos);
		if (block == null)
			return false;

		// Check if it's in the dirt tag but exclude grass and mycelium
		bool isDirtTag = TagsRegistry.Block.Dirt.Entries.Contains(block.RegistryId);
		bool isGrass = block.UnlocalizedName == "minecraft:grass_block";
		bool isMycelium = block.UnlocalizedName == "minecraft:mycelium";

		return isDirtTag && !isGrass && !isMycelium;
	}
}
