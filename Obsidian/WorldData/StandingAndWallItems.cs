using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>StandingAndWallBlockItem</c>: an item that places a standing block, or its wall block on a side, like a
/// torch or a standing sign. Which items those are, and their blocks and direction, come from
/// <c>Assets/item_attachments.json</c>, dumped from the fields of each item; the owner class must be one Obsidian
/// implements (<see cref="ImplementedOwners"/>).
/// </summary>
internal static class StandingAndWallItems
{
    private const string StandingAndWallBlockItem = "net.minecraft.world.item.StandingAndWallBlockItem";

    /// <summary>The Mojang classes whose <c>getPlacementState</c> Obsidian implements.</summary>
    private static readonly FrozenSet<string> ImplementedOwners = new[] { StandingAndWallBlockItem }.ToFrozenSet();

    private static readonly Lazy<FrozenDictionary<string, Attachment>> attachments = new(Load);

    /// <summary>What an item attaches: the standing block, the wall block, and the direction the standing block needs.</summary>
    internal sealed record Attachment(IBlock Standing, IBlock Wall, BlockFace Direction);

    /// <returns>Whether the item is one of vanilla's attaching items, whose owner Obsidian implements.</returns>
    public static bool TryGet(Item item, [NotNullWhen(true)] out Attachment? attachment) =>
        attachments.Value.TryGetValue(item.UnlocalizedName, out attachment);

    /// <summary>
    /// Vanilla <c>StandingAndWallBlockItem.getPlacementState</c>: of the nearest looking directions, skipping the one
    /// opposite the attachment, the first whose block would survive there is taken. The attachment direction takes the
    /// standing block's placement; any other takes the wall block's. A hanging sign item (vanilla
    /// <c>HangingSignItem.canPlace</c>) also needs something beside a wall hanging sign to hold it.
    /// </summary>
    /// <param name="context">The placement of the item's standing block.</param>
    /// <param name="getState">The placement of a block, by its class's <c>getStateForPlacement</c>.</param>
    /// <returns>The state to place, or null when nothing holds the item there.</returns>
    public static IBlock? Place(
        BlockPlaceContext context,
        Attachment attachment,
        Func<BlockPlaceContext, IBlock?> getState)
    {
        var wall = getState(context.WithBlock(attachment.Wall));
        foreach (var direction in context.NearestLookingDirections())
        {
            if (direction == attachment.Direction.Opposite())
                continue;

            var possible = direction == attachment.Direction
                ? getState(context.WithBlock(attachment.Standing))
                : wall;
            if (possible is not null && CanPlace(context.Level, possible, context.Position))
                return possible;
        }

        return null;
    }

    private static bool CanPlace(IBlockLevel level, IBlock state, Vector position)
    {
        var wallHangingSign = state.BlockClass() == "WallHangingSignBlock";
        if (wallHangingSign && !BlockPlacements.WallHangingSignHolds(level, state, position))
            return false;

        return PlacementSurvival.CanSurvive(level, state, position);
    }

    private static FrozenDictionary<string, Attachment> Load()
    {
        using var stream = typeof(StandingAndWallItems).Assembly.GetManifestResourceStream("Obsidian.Assets.item_attachments.json")
            ?? throw new InvalidOperationException("Missing item_attachments.json asset.");
        using var document = JsonDocument.Parse(stream);

        var table = new Dictionary<string, Attachment>();
        foreach (var item in document.RootElement.EnumerateObject())
        {
            var owner = item.Value.GetProperty("owner").GetString();
            if (owner is null || !ImplementedOwners.Contains(owner))
                continue;

            var standing = Block(item.Value.GetProperty("standing").GetString());
            var wall = Block(item.Value.GetProperty("wall").GetString());
            var direction = FeatureHelpers.ParseFace(item.Value.GetProperty("direction").GetString());
            table[item.Name] = new Attachment(standing, wall, direction);
        }

        return table.ToFrozenDictionary();
    }

    private static IBlock Block(string? identifier) =>
        BlocksRegistry.TryGetDefault(identifier ?? throw new InvalidOperationException("Attachment without a block."), out var block)
            ? block
            : throw new InvalidOperationException($"Attachment block {identifier} is not registered.");
}
