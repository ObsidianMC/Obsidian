using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

internal sealed partial class LevelBlockUpdates
{
    /// <summary>Rechecks vanilla updateShape survival for a queued shape destruction.</summary>
    internal bool IsUnsupported(Vector position, IBlock state) => updates.IsUnsupported(position, state);

}
