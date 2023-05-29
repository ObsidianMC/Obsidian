using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public BlockFace Face { get; private set; }

    [Field(3), DataFormat(typeof(float))]
    public VectorF Cursor { get; private set; }

    [Field(6)]
    public bool InsideBlock { get; private set; }

    [Field(7), VarLength]
    public int Sequence { get; private set; }

    public int Id => 0x31;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        //Get main hand first return offhand if null
        var currentItem = player.GetHeldItem() ?? player.GetOffHandItem();
        var position = this.Position;

        var b = await player.World.GetBlockAsync(position);

        if (b is not IBlock interactedBlock)
            return;

        if (TagsRegistry.Blocks.PlayersCanInteract.Entries.Contains(interactedBlock.RegistryId) && !player.Sneaking)
        {
            await server.Events.PlayerInteract.InvokeAsync(new PlayerInteractEventArgs(player)
            {
                Item = currentItem,
                Block = interactedBlock,
                BlockLocation = this.Position,
            });

            return;
        }

        var itemType = currentItem != null ? currentItem.Type : Material.Air;

        switch (itemType)
        {
            case Material.WaterBucket:
                itemType = Material.Water;
                break;
            case Material.LavaBucket:
                itemType = Material.Lava;
                break;
            case Material.Air:
                return;
            default:
                break;
        }

        IBlock block;
        try
        {
            block = BlocksRegistry.Get(itemType);
        }
        catch //item is not a block so just return
        {
            return;
        }

        if (player.Gamemode != Gamemode.Creative)
            player.Inventory.RemoveItem(player.inventorySlot, 1);

        switch (Face) // TODO fix this for logs
        {
            case BlockFace.Down:
                position.Y -= 1;
                break;

            case BlockFace.Up:
                position.Y += 1;
                break;

            case BlockFace.North:
                position.Z -= 1;
                break;

            case BlockFace.South:
                position.Z += 1;
                break;

            case BlockFace.West:
                position.X -= 1;
                break;

            case BlockFace.East:
                position.X += 1;
                break;

            default:
                break;
        }

        await player.World.SetBlockAsync(position, block, doBlockUpdate: true);
    }
}
