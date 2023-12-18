using Obsidian.API.BlockStates.Builders;
using Obsidian.Nbt;
using Obsidian.Nbt.Factory;
using System.IO;

namespace Obsidian.Registries;
internal static class StructureRegistry
{
    public static async Task InitializeAsync()
    {
        var structDir = "Assets\\Structures\\";
        var files = Directory.GetFiles(structDir, "*.nbt");
        foreach (var file in files)
        {
            byte[] nbtData = File.ReadAllBytes(file);
            await using var byteStream = new ReadOnlyStream(nbtData);
            var nbtReader = new NbtReader(byteStream, NbtCompression.GZip);
            var baseCompound = nbtReader.ReadNextTag() as NbtCompound;

            // Get palette
            List<IBlock> paletteBuffer = new();
            if (baseCompound!.TryGetTag("palette", out var palette))
            {
                var i = 0;
                foreach (NbtCompound entry in (palette as NbtList).Cast<NbtCompound>())
                {
                    var name = entry.GetString("Name");
                    var block = BlocksRegistry.Get(name);
                    if(entry.TryGetTag("Properties", out var props))
                    {
                        var thing = BlockStateBuilderFactory.Builder(entry);
/*                        Dictionary<string, string> blockProps = new();
                        foreach (var prop in props as NbtCompound)
                        {
                            
                        }*/
                    }
                    else
                    {
                        paletteBuffer.Add(block);
                    }
                }
                // private static readonly IBlock cocoa = BlocksRegistry.Get(Material.Cocoa, new CocoaStateBuilder().WithAge(2).WithFacing(Facing.South).Build());
}

            if (baseCompound.TryGetTag("blocks", out var blocks))
            {
                foreach (NbtCompound b in (blocks as NbtList).Cast<NbtCompound>())
                {
                    var state = b!.GetInt("state");
                    if (b!.TryGetTag("pos", out var coords))
                    {
                        var c = (NbtList)coords;
                        var x = c[1];
                        var y = c[1];
                        var z = c[2];
                    }
                }
            }
        }
    }
}
