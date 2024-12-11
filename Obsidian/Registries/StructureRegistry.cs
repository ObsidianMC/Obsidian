using Obsidian.Nbt;
using System.IO;

namespace Obsidian.Registries;
internal static class StructureRegistry
{
    public static readonly ConcurrentDictionary<string, Dictionary<Vector, IBlock>> storage = new();
    public static void Initialize()
    {
        var structDir = "Assets/Structures/";
        if(!Directory.Exists("Assets/Structures/"))
        {
            Directory.CreateDirectory("Assets/Structures");
        }
        var files = Directory.GetFiles(structDir, "*.nbt");
        foreach (var file in files)
        {
            var structureName = Path.GetFileNameWithoutExtension(file);
            storage[structureName] = new();
            byte[] nbtData = File.ReadAllBytes(file);
            using var byteStream = new ReadOnlyStream(nbtData);
            var nbtReader = new NbtReader(byteStream, NbtCompression.GZip);
            var baseCompound = nbtReader.ReadNextTag() as NbtCompound;

            // Get palette
            List<IBlock> paletteBuffer = new();
            if (baseCompound!.TryGetTag("palette", out var palette))
            {
                foreach (NbtCompound entry in (palette as NbtList).Cast<NbtCompound>())
                {
                    paletteBuffer.Add(entry.ToBlock());
                }
            }

            if (baseCompound.TryGetTag("blocks", out var blocks))
            {
                foreach (NbtCompound b in (blocks as NbtList).Cast<NbtCompound>())
                {
                    IBlock block = paletteBuffer[b!.GetInt("state")];
                    if (b!.TryGetTag("pos", out var coords))
                    {
                        var c = (NbtList)coords;
                        var offset = new Vector(
                            ((NbtTag<int>)c[0]).Value,
                            ((NbtTag<int>)c[1]).Value,
                            ((NbtTag<int>)c[2]).Value);

                        storage[structureName][offset] = block;
                    }
                }
            }
        }
    }
}
