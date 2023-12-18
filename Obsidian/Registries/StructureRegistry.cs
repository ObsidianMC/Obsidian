using Obsidian.Nbt;
using Obsidian.Nbt.Mappers;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace Obsidian.Registries;
internal static class StructureRegistry
{
    public static readonly ConcurrentDictionary<string, Dictionary<Vector3, IBlockState>> storage = new();
    public static void Initialize()
    {
        var structDir = "Assets\\Structures\\";
        var files = Directory.GetFiles(structDir, "*.nbt");
        foreach (var file in files)
        {
            byte[] nbtData = File.ReadAllBytes(file);
            using var byteStream = new ReadOnlyStream(nbtData);
            var nbtReader = new NbtReader(byteStream, NbtCompression.GZip);
            var baseCompound = nbtReader.ReadNextTag() as NbtCompound;

            // Get palette
            List<IBlockState> paletteBuffer = new();
            if (baseCompound!.TryGetTag("palette", out var palette))
            {
                foreach (NbtCompound entry in (palette as NbtList).Cast<NbtCompound>())
                {
                    paletteBuffer.Add(BlockStateBuilderMapper.GetFromNbt(entry) ?? BlocksRegistry.Air.State);
                }
            }

            if (baseCompound.TryGetTag("blocks", out var blocks))
            {
                foreach (NbtCompound b in (blocks as NbtList).Cast<NbtCompound>())
                {
                    var state = b!.GetInt("state");
                    IBlockState bs = paletteBuffer[state];
                    if (b!.TryGetTag("pos", out var coords))
                    {
                        var c = (NbtList)coords;
                        var x = c[0];
                        var y = c[1];
                        var z = c[2];
                    }
                }
            }
        }
    }
}
