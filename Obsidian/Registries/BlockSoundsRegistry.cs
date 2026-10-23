using Obsidian.API.Blocks;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's captured state-indexed sounds and the current sound types of plugin blocks, on both sides.
/// </summary>
internal static class BlockSoundsRegistry
{
    private static readonly Lazy<(int[] States, BlockSoundProperties[] Types)> data = new(Load);

    internal static BlockSoundProperties Get(int state)
    {
        if (BlocksRegistry.TryGetPluginBlock(state, out var block))
            return block.SoundType;

        var (states, types) = data.Value;
        return (uint)state < (uint)states.Length ? types[states[state]] : BlockSoundProperties.Stone;
    }

    private static (int[], BlockSoundProperties[]) Load()
    {
        using var stream = typeof(BlocksRegistry).Assembly
            .GetManifestResourceStream("Obsidian.Assets.block_sounds.json")
            ?? throw new InvalidOperationException("Missing captured vanilla block sounds.");
        using var json = JsonDocument.Parse(stream);
        var states = json.RootElement.GetProperty("states")
            .EnumerateArray()
            .Select(state => state.GetInt32())
            .ToArray();
        var types = json.RootElement.GetProperty("types")
            .EnumerateArray()
            .Select(type => new BlockSoundProperties
            {
                Volume = type.GetProperty("volume").GetSingle(),
                Pitch = type.GetProperty("pitch").GetSingle(),
                Break = Event(type, "break"),
                Step = Event(type, "step"),
                Place = Event(type, "place"),
                Hit = Event(type, "hit"),
                Fall = Event(type, "fall")
            })
            .ToArray();
        return (states, types);
    }

    private static SoundEvent Event(JsonElement type, string kind) =>
        new() { ResourceLocation = type.GetProperty(kind).GetString()! };
}
