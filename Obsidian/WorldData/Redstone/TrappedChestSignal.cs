namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Trapped chests (vanilla <c>TrappedChestBlock</c>): each half gives as much power as viewers have it open, at most
/// 15, and strongly powers only the block below it. Each half of a double chest counts its own viewers, so both halves
/// give power while the double chest is open.
/// </summary>
/// <remarks>
/// The count comes from the level's container lids (<see cref="IRedstoneLevel.TrappedChestViewers"/>), which also
/// update the neighbours of the chest and of the block below when it changes.
/// </remarks>
internal sealed class TrappedChestSignal(IRedstoneLevel level) : IRedstoneBehavior
{
    public const string BlockClass = "TrappedChestBlock";

    public static void Register(IRedstoneLevel level, RedstoneBehaviors redstone) =>
        redstone.Register(BlockClass, new TrappedChestSignal(level));

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        Math.Clamp(level.TrappedChestViewers(position), 0, RedstoneEngine.MaxSignal);

    // The direction is from the receiver to the chest: the block below the chest asks upward.
    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        direction == BlockFace.Up ? this.GetSignal(state, position, direction, wiresGiveSignal) : 0;
}
