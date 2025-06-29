using Obsidian.API.Inventory;
using Obsidian.Net;
using System.Buffers;
using System.IO.Hashing;

namespace Obsidian;
public sealed class HashedItemStack(Item holder, int count = 1) : IHashedItemStack
{
    public Dictionary<DataComponentType, int> HashedComponents { get; } = [];
    public List<DataComponentType> ComponentsToRemove { get; } = [];

    public int Count { get; set; } = count;

    public Item Holder { get; } = holder;

    public Material Type => this.Holder.Type;

    public bool Compare(ItemStack other)
    {
        if(other.Type != this.Type) 
            return false;

        var passed = true;
        var sharedBuffer = ArrayPool<byte>.Shared.Rent(1024 * 4);
        foreach(var (type, hash) in this.HashedComponents)
        {
            var writer = new NetworkBuffer(sharedBuffer);
            var component = other[type];

            component.Write(writer);

            var otherHash = Crc32.HashToUInt32(sharedBuffer.AsSpan(0, writer.Size));

            passed = hash == otherHash;

            if (!passed)
                return false;
        }

        ArrayPool<byte>.Shared.Return(sharedBuffer);

        return passed;
    }
}
