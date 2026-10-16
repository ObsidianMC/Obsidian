using Obsidian.API;

namespace Obsidian.WorldData;

/// <summary>Vanilla <c>DoublePlantBlock</c>: two-block plants (sunflower, lilac, peony, rose bush, pitcher plant, large fern).</summary>
internal static class DoublePlants
{
    /// <summary>The classes whose <c>playerWillDestroy</c> is <c>DoublePlantBlock</c>'s (<c>TallFlowerBlock</c> inherits it).</summary>
    public static bool IsTwoHigh(IBlock block) => block.BlockClass() is "DoublePlantBlock" or "TallFlowerBlock";

    /// <summary>
    /// Vanilla <c>DoublePlantBlock.preventDropFromBottomPart</c>, which a creative break runs before removing the upper half:
    /// the lower half goes silently, with no drop. In survival the lower half is removed by its shape update and drops its
    /// loot, and the upper half drops nothing (its loot needs <c>half=lower</c>).
    /// </summary>
    public static async ValueTask PreventDropFromBottomPartAsync(AbstractLevel level, IBlock block, Vector position)
    {
        if (!IsTwoHigh(block) || block.GetProperty("half") != "upper")
            return;

        var below = position + Vector.Down;
        if (await level.GetBlockAsync(below) is not { } lower || lower.UnlocalizedName != block.UnlocalizedName || lower.GetProperty("half") != "lower")
            return;

        await level.DestroyBlockAsync(below, cause: null, drop: false);
    }
}
