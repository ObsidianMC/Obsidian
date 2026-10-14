using Obsidian.API.Blocks;

namespace Obsidian.WorldData;

/// <summary>
/// Prepares vanilla two-part destruction without a general shape-update system. Evaluate DropState at DropPosition
/// before removing either part, then remove the verified partner along with the selected block.
/// </summary>
internal sealed record TwoPartBlockBreak(
    Vector DropPosition,
    IBlock DropState,
    Vector? PartnerPosition = null,
    IBlock? PartnerReplacement = null,
    IBlock? ExpectedPartner = null)
{
    internal static async ValueTask<TwoPartBlockBreak> PrepareAsync(ILevel level, Vector position, IBlock block)
    {
        var single = new TwoPartBlockBreak(position, block);
        if (!block.UnlocalizedName.StartsWith("minecraft:", StringComparison.Ordinal))
            return single;

        var properties = block.GetProperties();
        string property;
        string other;
        string dropPart;
        Vector offset;
        if (properties.GetValueOrDefault("half") is "lower" or "upper")
        {
            // Vanilla's doors and double plants use lower/upper; stairs and slabs use bottom/top instead.
            property = "half";
            other = properties[property] == "lower" ? "upper" : "lower";
            dropPart = "lower";
            offset = other == "upper" ? Vector.Up : Vector.Down;
        }
        else if (properties.GetValueOrDefault("part") is "head" or "foot")
        {
            property = "part";
            other = properties[property] == "head" ? "foot" : "head";
            dropPart = "head";
            offset = properties.GetValueOrDefault("facing") switch
            {
                "north" => new Vector(0, 0, -1),
                "south" => new Vector(0, 0, 1),
                "east" => new Vector(1, 0, 0),
                "west" => new Vector(-1, 0, 0),
                _ => Vector.Zero
            };
            if (offset == Vector.Zero)
                return single;

            if (other == "foot")
                offset *= -1;
        }
        else
        {
            return single;
        }

        var partnerPosition = position + offset;
        var partner = await level.GetBlockAsync(partnerPosition);
        if (partner is null || partner.RegistryId != block.RegistryId)
            return single;

        var partnerProperties = partner.GetProperties();
        if (partnerProperties.GetValueOrDefault(property) != other
            || property == "part" && partnerProperties.GetValueOrDefault("facing") != properties["facing"])
            return single;

        // Vanilla suppresses the bottom drop when a creative player breaks the top of a double plant. Choosing
        // one drop-bearing state also prevents duplicate drops in survival and when both parts are hit by a blast.
        var replacement = partnerProperties.GetValueOrDefault("waterlogged") == "true"
            || partner.Is(BlockIds.TallSeagrass) ? BlocksRegistry.Water : BlocksRegistry.Air;
        return properties[property] == dropPart
            ? new TwoPartBlockBreak(position, block, partnerPosition, replacement, partner)
            : new TwoPartBlockBreak(partnerPosition, partner, partnerPosition, replacement, partner);
    }

    internal async ValueTask RemovePartnerAsync(ILevel level)
    {
        if (this.PartnerPosition is Vector position && level is AbstractLevel concrete)
        {
            await concrete.TrySetBlockAsync(position, this.PartnerReplacement!, BlockUpdateFlags.All,
                current => current.GetHashCode() == this.ExpectedPartner!.GetHashCode());
        }
    }
}
