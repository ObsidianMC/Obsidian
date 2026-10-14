using Obsidian.API.World;

namespace Obsidian.WorldData;

public partial class AbstractLevel
{
    public ValueTask<bool> TryLightPortalAsync(PortalType type, Vector position) =>
        this.Portals.TryIgniteAsync(position, type);
}
