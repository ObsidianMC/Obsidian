using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.WorldData.Features;
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

    /// <summary>The state an item places, and whether it's the wall block.</summary>
    internal readonly record struct Placement(IBlock State, bool OnWall);

    /// <returns>Whether the item is one of vanilla's attaching items, whose owner Obsidian implements.</returns>
    public static bool TryGet(Item item, [NotNullWhen(true)] out Attachment? attachment) =>
        attachments.Value.TryGetValue(item.UnlocalizedName, out attachment);

    /// <summary>
    /// Vanilla <c>StandingAndWallBlockItem.getPlacementState</c>: of the nearest looking directions, skipping the one
    /// opposite the attachment, the first where its block holds is taken. The attachment direction takes the
    /// standing block; any other horizontal one takes the wall block, facing away from the wall.
    /// </summary>
    /// <returns>The placement, or null when nothing holds the item there.</returns>
    public static Placement? Place(LevelBlockUpdates level, Attachment attachment, Vector position, IPlayer player,
        BlockFace face, bool replacesClicked)
    {
        foreach (var direction in PlacementDirections.NearestLooking(player.Yaw.Degrees, player.Pitch.Degrees, face, replacesClicked))
        {
            if (direction == attachment.Direction.Opposite())
                continue;

            if (direction == attachment.Direction)
            {
                if (PlacementSurvival.CanAttach(level, attachment.Standing, position))
                    return new Placement(attachment.Standing, OnWall: false);

                continue;
            }

            if (direction.IsVertical())
                continue;

            var wall = attachment.Wall.WithProperty("facing", FeatureHelpers.FaceName(direction.Opposite()));
            if (PlacementSurvival.CanAttach(level, wall, position))
                return new Placement(VanillaPlacement.Waterlog(level, wall, position), OnWall: true);
        }

        return null;
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
