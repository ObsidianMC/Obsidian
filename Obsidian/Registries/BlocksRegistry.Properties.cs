namespace Obsidian.Registries;
internal partial class BlocksRegistry
{
    private static readonly Dictionary<string, IBlock> defaultBlockCache = [];

    private static readonly Dictionary<string, string> resourceIdToName = [];
}
