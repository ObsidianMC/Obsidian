using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Registries;
/// <summary>
/// Provides access to synchronized registry entries by namespaced identifier or numeric ID.
/// </summary>
public static partial class CodecRegistry
{
    /// <summary>
    /// Looks up a chat type by identifier, returning whether an entry was found.
    /// </summary>
    public static bool TryGetChatType(string resourceId, [MaybeNullWhen(false)] out ChatTypeCodec? codec) =>
        ChatType.All.TryGetValue(resourceId, out codec);

    /// <summary>
    /// Looks up a biome by identifier or numeric registry ID, returning whether an entry was found.
    /// </summary>
    public static bool TryGetBiome(string resourceId, [MaybeNullWhen(false)] out BiomeCodec? codec) =>
        Biomes.All.TryGetValue(resourceId, out codec);

    /// <summary>
    /// Looks up a biome by identifier or numeric registry ID, returning whether an entry was found.
    /// </summary>
    public static bool TryGetBiome(int registryId, [MaybeNullWhen(false)] out BiomeCodec? codec) =>
       Biomes.ById.TryGetValue(registryId, out codec);

    /// <summary>
    /// Gets a biome by numeric registry ID, or <c>null</c> when the ID is unknown.
    /// </summary>
    public static BiomeCodec? GetBiome(int registryId) =>
        Biomes.ById.GetValueOrDefault(registryId);

    /// <summary>
    /// Looks up a dimension type by identifier, returning whether an entry was found.
    /// </summary>
    public static bool TryGetDimension(string resourceId, [MaybeNullWhen(false)] out DimensionCodec? codec) =>
        Dimensions.All.TryGetValue(resourceId, out codec);
}

