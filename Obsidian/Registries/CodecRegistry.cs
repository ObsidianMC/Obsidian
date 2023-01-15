using Obsidian.API.Registry.Codecs.Dimensions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Registries;
public partial class CodecRegistry
{
    public static bool TryGetDimension(int id, [MaybeNullWhen(false)] out DimensionCodec? codec) => Dimensions.TryGetValue(id, out codec);
    public static bool TryGetDimension(string name, [MaybeNullWhen(false)] out DimensionCodec? codec)
    {
        var (_, value) = Dimensions.FirstOrDefault(x => x.Value.Name.EqualsIgnoreCase(name));

        if (value is not DimensionCodec dimensionCodec)
        {
            codec = null;
            return false;
        }

        codec = dimensionCodec;

        return true;
    }
}
