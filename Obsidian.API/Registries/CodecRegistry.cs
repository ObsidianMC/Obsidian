using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Registries;
public static partial class CodecRegistry
{
    public static bool TryGetChatType(string resourceId, [MaybeNullWhen(false)] out ChatTypeCodec? codec) =>
        ChatType.All.TryGetValue(resourceId, out codec);

    public static bool TryGetBiome(string resourceId, [MaybeNullWhen(false)] out BiomeCodec? codec) =>
        Biomes.All.TryGetValue(resourceId, out codec);

    public static bool TryGetDimension(string resourceId, [MaybeNullWhen(false)] out DimensionCodec? codec) =>
        Dimensions.All.TryGetValue(resourceId, out codec);
}

