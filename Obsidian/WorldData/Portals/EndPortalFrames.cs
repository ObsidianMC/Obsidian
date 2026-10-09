namespace Obsidian.WorldData.Portals;

internal static class EndPortalFrames
{
    // Origin is the northwest block of the 3 by 3 portal interior.
    public static IEnumerable<(Vector Position, string Facing)> Ring(Vector origin)
    {
        for (var i = 0; i < 3; i++)
        {
            yield return (origin + new Vector(i, 0, -1), "south");
            yield return (origin + new Vector(i, 0, 3), "north");
            yield return (origin + new Vector(-1, 0, i), "east");
            yield return (origin + new Vector(3, 0, i), "west");
        }
    }

    public static async ValueTask<bool> IsCompleteAsync(Func<Vector, ValueTask<IBlock?>> read, Vector origin)
    {
        foreach (var (position, facing) in Ring(origin))
        {
            var block = await read(position);
            if (block?.Material != Material.EndPortalFrame || block.GetProperty("eye") != "true" ||
                block.GetProperty("facing") != facing)
                return false;
        }
        return true;
    }
}
