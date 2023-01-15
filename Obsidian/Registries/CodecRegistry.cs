using Obsidian.API.Registry.Codecs.Dimensions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Registries;
public partial class CodecRegistry
{
    public static bool TryGetDimension(string name, [MaybeNullWhen(false)] out DimensionCodec? codec) =>
        Dimensions.All.TryGetValue(name, out codec);
}
