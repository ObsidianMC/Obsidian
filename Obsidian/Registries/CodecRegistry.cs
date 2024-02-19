using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Registries;
public static partial class CodecRegistry
{
    private const string AssetsNamespace = "Obsidian.Assets";

    internal static bool Initialized { get; private set; }

    public static bool TryGetChatType(string resourceId, [MaybeNullWhen(false)] out ChatCodec codec) =>
        ChatType.All.TryGetValue(resourceId, out codec);

    public static bool TryGetBiome(string resourceId, [MaybeNullWhen(false)] out BiomeCodec codec) =>
        Biomes.All.TryGetValue(resourceId, out codec);

    public static bool TryGetDimension(string resourceId, [MaybeNullWhen(false)] out DimensionCodec codec) =>
        Dimensions.All.TryGetValue(resourceId, out codec);

    public static async Task InitializeAsync(ILogger logger)
    {
        logger.LogInformation("Parsing codecs...");

        await Biomes.InitalizeAsync();
        await ChatType.InitalizeAsync();
        await DamageTypes.InitalizeAsync();
        await Dimensions.InitalizeAsync();
        await TrimMaterials.InitalizeAsync();
        await TrimPatterns.InitalizeAsync();

        Initialized = true;

        logger.LogInformation("All codecs loaded.");
    }
}
