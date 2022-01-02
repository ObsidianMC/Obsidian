using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerBlockPlacement : IServerboundPacket
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

    public int Id => 0x2E;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        //Get main hand first return offhand if null
        var currentItem = player.GetHeldItem() ?? player.GetOffHandItem();
        var position = this.Position;

        var b = server.World.GetBlock(position);
        if (b is null) { return; }
        var interactedBlock = (Block)b;

        //TODO check if a player can place a block if they can't then call the player interact event
        if (interactedBlock.IsInteractable && !player.Sneaking)
        {
            await server.Events.InvokePlayerInteractAsync(new PlayerInteractEventArgs(player)
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

        Block block;
        try
        {
            block = Registry.GetBlock(itemType);
        }
        catch //item is not a block so just return
        {
            return;
        }

        if (player.Gamemode != Gamemode.Creative)
            player.Inventory.RemoveItem(player.inventorySlot, 1);

        switch (Face) // TODO fix this for logs
        {
            case BlockFace.Bottom:
                position.Y -= 1;
                break;

            case BlockFace.Top:
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

        // TODO calculate the block state
        server.World.SetBlock(position, block, doBlockUpdate: true);
    }
}
