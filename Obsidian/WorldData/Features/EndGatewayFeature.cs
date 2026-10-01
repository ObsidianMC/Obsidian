namespace Obsidian.WorldData.Features;

/// <summary>
/// An end gateway in its bedrock frame, like vanilla's EndGatewayFeature.
/// </summary>
/// <remarks>
/// Vanilla also stores <see cref="Exit"/> in the gateway's block entity; Obsidian has no end gateway block entity yet, so only the
/// blocks are placed (vanilla draws no randomness here).
/// </remarks>
[ConfiguredFeatureClass("minecraft:end_gateway")]
public sealed class EndGatewayFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:end_gateway";

    /// <summary>
    /// Where the gateway teleports to; <c>null</c> to search for an exit when first used.
    /// </summary>
    public Vector? Exit { get; init; }

    /// <summary>
    /// Whether players arrive exactly at <see cref="Exit"/> instead of a safe spot near it.
    /// </summary>
    public required bool Exact { get; init; }

    private static IBlock Gateway => field ??= BlocksRegistry.Get(Material.EndGateway);

    private static IBlock Bedrock => field ??= BlocksRegistry.Get(Material.Bedrock);

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        foreach (var position in FeatureHelpers.BetweenClosed(origin + new Vector(-1, -2, -1), origin + new Vector(1, 2, 1)))
        {
            var centerX = position.X == origin.X;
            var centerY = position.Y == origin.Y;
            var centerZ = position.Z == origin.Z;
            var cap = Math.Abs(position.Y - origin.Y) == 2;

            IBlock block;
            if (centerX && centerY && centerZ)
                block = Gateway;
            else if (centerY)
                block = Air;
            else if (cap && centerX && centerZ)
                block = Bedrock;
            else if ((centerX || centerZ) && !cap)
                block = Bedrock;
            else
                block = Air;

            level.SetBlock(position, block);
        }

        return true;
    }
}
