using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class WindowItems : IClientboundPacket
{
    [Field(0)]
    public byte WindowId { get; }

    [Field(1), VarLength]
    public int StateId { get; set; }

    [Field(2)]
    public List<ItemStack> Items { get; }

    [Field(3)]
    public ItemStack CarriedItem { get; set; }

    public int Id => 0x14;

    public WindowItems(byte windowId, List<ItemStack> items)
    {
        WindowId = windowId;
        Items = items;
    }
}
