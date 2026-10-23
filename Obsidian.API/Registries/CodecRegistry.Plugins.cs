using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Collections.Frozen;

namespace Obsidian.API.Registries;

// The codec registries plugins add to from their data files (see CodecTable). The generated parts hold vanilla's
// entries, as static properties with their ids, and the table.
public static partial class CodecRegistry
{
    public static partial class Biomes
    {
        /// <summary>
        /// The biomes by name, vanilla's and plugins'.
        /// </summary>
        public static FrozenDictionary<string, BiomeCodec> All => table.All;

        /// <summary>
        /// The biomes by id, vanilla's and plugins'.
        /// </summary>
        public static FrozenDictionary<int, BiomeCodec> ById => table.ById;

        /// <summary>
        /// The bits per entry of the global biome palette, from the number of biomes, plugins' included.
        /// </summary>
        public static int GlobalBitsPerEntry => table.GlobalBitsPerEntry;

        internal static CodecTable<BiomeCodec> Table => table;
    }

    public static partial class Dimensions
    {
        /// <summary>
        /// The dimension types by name, vanilla's and plugins'.
        /// </summary>
        public static FrozenDictionary<string, DimensionCodec> All => table.All;

        /// <summary>
        /// The dimension types by id, vanilla's and plugins'.
        /// </summary>
        public static FrozenDictionary<int, DimensionCodec> ById => table.ById;

        /// <summary>
        /// The bits a global palette of dimension types would use, from their number, plugins' included.
        /// </summary>
        public static int GlobalBitsPerEntry => table.GlobalBitsPerEntry;

        internal static CodecTable<DimensionCodec> Table => table;
    }

    public static partial class DamageType
    {
        /// <summary>
        /// The damage types by name, vanilla's and plugins'.
        /// </summary>
        public static FrozenDictionary<string, DamageTypeCodec> All => table.All;

        /// <summary>
        /// The damage types by id, vanilla's and plugins'.
        /// </summary>
        public static FrozenDictionary<int, DamageTypeCodec> ById => table.ById;

        /// <summary>
        /// The bits a global palette of damage types would use, from their number, plugins' included.
        /// </summary>
        public static int GlobalBitsPerEntry => table.GlobalBitsPerEntry;

        internal static CodecTable<DamageTypeCodec> Table => table;
    }
}
